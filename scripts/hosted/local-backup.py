"""Provision private, independent local restic repositories without replacing keys."""
import argparse
import os
from pathlib import Path
import re
import secrets
import stat
import subprocess


def check_path(path):
    """Reject symlinks in every existing component, including parent directories."""
    for component in [*reversed(path.parents), path]:
        if component.is_symlink():
            raise ValueError(f'Backup paths must not contain symlinks: {component}')


def private(path, mode, directory):
    check_path(path)
    info = path.stat()
    if (stat.S_IMODE(info.st_mode) != mode or info.st_uid != os.geteuid()
            or (directory and not stat.S_ISDIR(info.st_mode))
            or (not directory and (not stat.S_ISREG(info.st_mode) or info.st_nlink != 1 or not info.st_size))):
        raise ValueError(f'Backup path must be owned by the running user and mode {mode:o}: {path}')


def prepare(project, state, initialize=False, backup_root=Path('/srv/rabbithole/backups'),
            key_root=Path('/etc/rabbithole/backup-keys')):
    if not re.fullmatch(r'rabbithole-(hosted|staging|rehearsal-[a-z0-9-]+)', project):
        raise ValueError('Invalid isolated hosted project name.')
    state = Path(state)
    roots = [Path(backup_root), Path(key_root)]
    if not state.is_absolute() or any(not p.is_absolute() for p in roots):
        raise ValueError('Backup and state paths must be absolute.')
    if roots[0].resolve().is_relative_to(roots[1].resolve()) or roots[1].resolve().is_relative_to(roots[0].resolve()):
        raise ValueError('Repository and encryption key roots must be separate.')
    for root in roots:
        check_path(root / project)
        if root.resolve().is_relative_to(state.resolve()) or state.resolve().is_relative_to(root.resolve()):
            raise ValueError('Backup repositories and keys must be outside STATE_PATH and must not contain it.')
        for directory in (root, root / project):
            if directory.exists():
                private(directory, 0o700, True)
    pairs = [(roots[0] / project / category, roots[1] / project / (category + '.password'))
             for category in ('content', 'recovery')]
    # Complete preflight for both repositories before creating either key.
    for repository, key in pairs:
        check_path(repository)
        check_path(key)
        if repository.exists():
            private(repository, 0o700, True)
            if not (repository / 'config').is_file() and any(repository.iterdir()):
                raise ValueError(f'Existing repository has no config; refusing initialization: {repository}')
        if (repository / 'config').is_symlink():
            raise ValueError('Repository config must not be a symlink.')
        if key.exists():
            private(key, 0o600, False)
        elif repository.exists() and any(repository.iterdir()):
            raise ValueError(f'Existing repository encryption key is missing; restore it: {key}')
        if not initialize and (not (repository / 'config').is_file() or not key.exists()):
            raise ValueError('Run backup.sh to initialize local backup repositories first.')
    if all(key.exists() for _, key in pairs) and pairs[0][1].read_bytes() == pairs[1][1].read_bytes():
        raise ValueError('Content and recovery encryption passwords must differ.')
    if not initialize:
        return
    for root in roots:
        root.mkdir(parents=True, mode=0o700, exist_ok=True)
        (root / project).mkdir(mode=0o700, exist_ok=True)
    environment = {k: v for k, v in os.environ.items() if not k.startswith('RESTIC_')}
    for repository, key in pairs:
        if not key.exists():
            # O_EXCL prevents replacing an existing key even if another process races.
            with open(key, 'x', opener=lambda path, flags: os.open(path, flags, 0o600)) as stream:
                stream.write(secrets.token_urlsafe(48) + '\n')
        if not (repository / 'config').is_file():
            repository.mkdir(mode=0o700, exist_ok=True)
            subprocess.run(['restic', '--repo', str(repository), '--password-file', str(key), 'init'],
                           check=True, env=environment, stdout=subprocess.DEVNULL)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--project', required=True)
    parser.add_argument('--state', required=True)
    parser.add_argument('--initialize', action='store_true')
    args = parser.parse_args()
    try:
        prepare(args.project, args.state, args.initialize)
    except (OSError, ValueError, subprocess.CalledProcessError) as error:
        raise SystemExit('Local backup configuration rejected: ' + str(error))
