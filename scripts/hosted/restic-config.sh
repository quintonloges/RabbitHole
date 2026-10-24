# Sourced only by trusted server operations. Never execute a release-provided secret config.
[[ $# == 0 || ($# == 1 && "$1" == --initialize) ]] || { echo 'Invalid backup configuration arguments.' >&2; exit 1; }
BACKUP_MODE=local
if grep -q '^BACKUP_MODE=' "$RH_CONFIG"; then BACKUP_MODE=$(sed -n 's/^BACKUP_MODE=//p' "$RH_CONFIG"); fi
# Accept matching dotenv quotes, but reject empty, duplicate or malformed values.
case "$BACKUP_MODE" in '"local"'|"'local'") BACKUP_MODE=local;; '"offserver"'|"'offserver'") BACKUP_MODE=offserver;; esac
case "$BACKUP_MODE" in
  local)
    # Fixed paths stay outside the state directory mounted into the public gateway.
    RESTIC_REPOSITORY="/srv/rabbithole/backups/$PROJECT_NAME/content"
    RECOVERY_REPOSITORY="/srv/rabbithole/backups/$PROJECT_NAME/recovery"
    RESTIC_PASSWORD_FILE="/etc/rabbithole/backup-keys/$PROJECT_NAME/content.password"
    RECOVERY_PASSWORD_FILE="/etc/rabbithole/backup-keys/$PROJECT_NAME/recovery.password"
    unset RESTIC_PASSWORD RESTIC_PASSWORD_COMMAND RESTIC_REPOSITORY_FILE
    python3 "$(dirname "${BASH_SOURCE[0]}")/local-backup.py" --project "$PROJECT_NAME" --state "$STATE_PATH" "$@"
    ;;
  offserver)
    backup_env=${RH_BACKUP_CONFIG:-/etc/rabbithole/backup.env}
    [[ -f "$backup_env" && ! -L "$backup_env" && $(stat -c '%a' "$backup_env") == 600 ]] || { echo 'Off-server mode requires private backup.env (600).' >&2; exit 1; }
    case "$(realpath "$backup_env")/" in "$STATE_PATH/"*) echo 'Backup configuration must be outside STATE_PATH.' >&2; exit 1;; esac
    set -a; source "$backup_env"; set +a
    : "${RESTIC_REPOSITORY:?Configure an off-server backup destination}"
    : "${RESTIC_PASSWORD_FILE:?Store the repository encryption password separately}"
    : "${RECOVERY_REPOSITORY:?Configure a separate recovery repository}"
    : "${RECOVERY_PASSWORD_FILE:?Set a separate recovery encryption password}"
    [[ "$RESTIC_REPOSITORY" =~ ^(sftp:|s3:|rest:https://|azure:|gs:|b2:) && "$RECOVERY_REPOSITORY" =~ ^(sftp:|s3:|rest:https://|azure:|gs:|b2:) ]] || { echo 'Off-server mode requires two off-server encrypted destinations.' >&2; exit 1; }
    [[ "$RESTIC_REPOSITORY" != "$RECOVERY_REPOSITORY" && $(realpath -m "$RESTIC_PASSWORD_FILE") != $(realpath -m "$RECOVERY_PASSWORD_FILE") ]] || { echo 'Recovery requires an independent repository and password.' >&2; exit 1; }
    for password_file in "$RESTIC_PASSWORD_FILE" "$RECOVERY_PASSWORD_FILE"; do
      [[ -f "$password_file" && ! -L "$password_file" && -s "$password_file" && $(stat -c '%a' "$password_file") == 600 ]] || { echo 'Restic password files must exist with permissions 600.' >&2; exit 1; }
      case "$(realpath "$password_file")/" in "$STATE_PATH/"*) echo 'Backup passwords must be outside STATE_PATH.' >&2; exit 1;; esac
    done
    if cmp -s "$RESTIC_PASSWORD_FILE" "$RECOVERY_PASSWORD_FILE"; then echo 'Recovery requires a different encryption password.' >&2; exit 1; fi
    ;;
  *) echo 'BACKUP_MODE must be local or offserver.' >&2; exit 1;;
esac
export RESTIC_REPOSITORY RESTIC_PASSWORD_FILE
