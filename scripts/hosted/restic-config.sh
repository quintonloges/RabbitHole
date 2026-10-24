# Sourced only by trusted server operations. Never execute a release-provided secret config.
backup_env=${RH_BACKUP_CONFIG:-/etc/rabbithole/backup.env}
[[ -f "$backup_env" && $(stat -c '%a' "$backup_env") == 600 ]] || { echo 'Set private backup.env (600) first.' >&2; exit 1; }
set -a; source "$backup_env"; set +a
: "${RESTIC_REPOSITORY:?Configure an off-server backup destination}"
: "${RESTIC_PASSWORD_FILE:?Store the repository encryption password separately}"
: "${RECOVERY_REPOSITORY:?Configure a separate recovery repository}"
: "${RECOVERY_PASSWORD_FILE:?Set a separate recovery encryption password}"
[[ "$RESTIC_REPOSITORY" =~ ^(sftp:|s3:|rest:https://|azure:|gs:|b2:) && "$RECOVERY_REPOSITORY" =~ ^(sftp:|s3:|rest:https://|azure:|gs:|b2:) ]] || { echo 'Both repositories must be off-server encrypted destinations.' >&2; exit 1; }
[[ "$RESTIC_REPOSITORY" != "$RECOVERY_REPOSITORY" && "$RESTIC_PASSWORD_FILE" != "$RECOVERY_PASSWORD_FILE" ]] || { echo 'Recovery requires an independent repository and password.' >&2; exit 1; }
for password_file in "$RESTIC_PASSWORD_FILE" "$RECOVERY_PASSWORD_FILE"; do
  [[ -f "$password_file" && $(stat -c '%a' "$password_file") == 600 ]] || { echo 'Restic password files must exist with permissions 600.' >&2; exit 1; }
done
