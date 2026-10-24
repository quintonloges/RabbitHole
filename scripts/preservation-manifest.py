"""Hash database records and uploaded files without placing their contents in reports.

Usage: preservation-manifest.py --output manifest.json --uploads uploads [--baseline before.json]
       -- docker compose ... exec -T db psql -U rabbithole -d rabbithole -Atqc
The command after -- must accept one SQL argument. Never pass credentials in it.
"""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import sys


def capture(command, uploads, baseline=None):
    def query(sql):
        result = subprocess.run([*command, sql], check=True, capture_output=True, text=True, encoding='utf-8')
        return json.loads(result.stdout)

    schema = query("SELECT coalesce(json_object_agg(table_name, cols), '{}'::json) FROM "
                   "(SELECT table_name, json_agg(column_name ORDER BY ordinal_position) cols "
                   "FROM information_schema.columns WHERE table_schema='public' "
                   "AND table_name <> '__EFMigrationsHistory' GROUP BY table_name) s")
    tables = {}
    for table, columns in (baseline['tables'].items() if baseline else schema.items()):
        columns = columns['columns'] if baseline else columns
        if table not in schema or not set(columns).issubset(schema[table]):
            raise ValueError('Missing table/columns after restore: ' + table)
        quote = lambda name: '"' + name.replace('"', '""') + '"'
        rows = query('SELECT coalesce(json_agg(t), \'[]\'::json) FROM (SELECT '
                     + ','.join(map(quote, columns)) + ' FROM ' + quote(table) + ') t')
        # New internal upgrade markers are expected; existing settings must be byte-for-byte preserved.
        if table == 'Settings' and baseline:
            keys = set(baseline['tables'][table].get('keys', []))
            rows = [row for row in rows if row['Key'] in keys]
        encoded = sorted(json.dumps(row, sort_keys=True, separators=(',', ':'), ensure_ascii=False) for row in rows)
        tables[table] = {'columns': columns, 'count': len(rows),
                         'sha256': hashlib.sha256('\n'.join(encoded).encode()).hexdigest()}
        if table == 'Settings':
            tables[table]['keys'] = sorted(row['Key'] for row in rows)
    files = {}
    for file in sorted(Path(uploads).rglob('*')):
        if file.is_symlink():
            raise ValueError('Upload symlinks are not permitted in a preservation check.')
        if file.is_file():
            with file.open('rb') as stream:
                files[file.relative_to(uploads).as_posix()] = hashlib.file_digest(stream, 'sha256').hexdigest()
    result = {'version': 1, 'tables': tables, 'files': files}
    if baseline and result != baseline:
        differences = [name for name in tables if tables[name] != baseline['tables'].get(name)]
        if files != baseline['files']:
            differences.append('uploaded files')
        raise ValueError('Preservation comparison failed: ' + ', '.join(differences))
    return result


def main():
    split = sys.argv.index('--')
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', required=True)
    parser.add_argument('--uploads', required=True)
    parser.add_argument('--baseline')
    args = parser.parse_args(sys.argv[1:split])
    command = sys.argv[split + 1:]
    if not command:
        parser.error('A psql command after -- is required.')
    baseline = json.loads(Path(args.baseline).read_text()) if args.baseline else None
    result = capture(command, args.uploads, baseline)
    Path(args.output).write_text(json.dumps(result, indent=2, sort_keys=True), encoding='utf-8')
    print(f"Verified {len(result['tables'])} tables and {len(result['files'])} uploaded files.")


if __name__ == '__main__':
    main()
