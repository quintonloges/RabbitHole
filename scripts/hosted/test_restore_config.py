"""Exercise the actual restore-test configuration heredoc without Docker."""
import ipaddress
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


SCRIPT = Path(__file__).with_name('restore-test.sh').read_text()
MARKER = 'python3 - "$RH_CONFIG" "$work/hosted.env" "$work/networks.json" "$test_project" "$work/state" <<\'PY\'\n'
CONFIG_BLOCK = SCRIPT.split(MARKER, 1)[1].split('\nPY\n', 1)[0]


class RestoreConfigurationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.source = self.root / 'source.env'
        self.target = self.root / 'rehearsal.env'
        self.networks = self.root / 'networks.json'
        self.original = ('# Fixture configuration\nPUBLIC_HOST=fixture.example\n'
                         'POSTGRES_PASSWORD=fixture-only\nCUSTOM_SETTING=preserve=value\n'
                         'PROJECT_NAME=rabbithole-hosted\nSTATE_PATH=/srv/rabbithole/state\n'
                         'APP_SUBNET=172.30.5.0/24\nGATEWAY_IP=172.30.5.2\n'
                         'WEB_IP=172.30.5.3\nAPI_IP=172.30.5.4\n'
                         'INGESTION_PAUSED=false\nPUBLIC_INDEXING_ENABLED=true\n')
        self.source.write_text(self.original)

    def generate(self, networks):
        self.networks.write_text(json.dumps(networks))
        return subprocess.run([sys.executable, '-c', CONFIG_BLOCK, str(self.source), str(self.target),
                               str(self.networks), 'rabbithole-rehearsal-fixture', '/var/tmp/fixture-state'],
                              capture_output=True, text=True, timeout=10)

    def test_null_builtin_network_config_skips_overlapping_subnets(self):
        networks = [{'Name': 'host', 'IPAM': {'Driver': 'default', 'Config': None}},
                    {'Name': 'none', 'IPAM': None},
                    {'Name': 'missing-ipam'},
                    {'Name': 'bridge', 'IPAM': {'Config': [{'Subnet': '172.29.1.0/24'}]}},
                    {'Name': 'existing-project', 'IPAM': {'Config': [{'Subnet': '172.29.2.0/23'},
                                                                   {'Subnet': 'fd00::/64'}]}},
                    {'Name': 'empty-config', 'IPAM': {'Config': []}}]
        result = self.generate(networks)
        self.assertEqual(result.returncode, 0, result.stderr)
        lines = self.target.read_text().splitlines()
        config = dict(line.split('=', 1) for line in lines if line and not line.startswith('#'))
        self.assertEqual(config['APP_SUBNET'], '172.29.4.0/24')
        chosen = ipaddress.ip_network(config['APP_SUBNET'])
        for subnet in ('172.29.1.0/24', '172.29.2.0/23', 'fd00::/64'):
            self.assertFalse(chosen.overlaps(ipaddress.ip_network(subnet)))
        self.assertEqual(config['GATEWAY_IP'], '172.29.4.2')
        self.assertEqual(config['WEB_IP'], '172.29.4.3')
        self.assertEqual(config['API_IP'], '172.29.4.4')
        self.assertEqual(config['PROJECT_NAME'], 'rabbithole-rehearsal-fixture')
        self.assertEqual(config['STATE_PATH'], '/var/tmp/fixture-state')
        self.assertEqual(config['INGESTION_PAUSED'], 'true')
        self.assertEqual(config['PUBLIC_INDEXING_ENABLED'], 'false')
        self.assertEqual(config['STAGING_ORIGIN'], 'https://rabbithole.quintonloges.me:47834')
        self.assertEqual(lines[:4], self.original.splitlines()[:4])
        self.assertEqual(self.source.read_text(), self.original)
        self.assertEqual(sum(line.startswith('APP_SUBNET=') for line in lines), 1)

    def test_no_configured_subnets_uses_first_candidate(self):
        for networks in ([], [{'IPAM': None}], [{'IPAM': {'Config': None}}], [{'IPAM': {}}]):
            with self.subTest(networks=networks):
                result = self.generate(networks)
                self.assertEqual(result.returncode, 0, result.stderr)
                self.assertIn('APP_SUBNET=172.29.1.0/24\n', self.target.read_text())

    def test_exhausted_subnets_refuse_to_generate_config(self):
        result = self.generate([{'IPAM': {'Config': [{'Subnet': '172.29.0.0/16'}]}}])
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('No free restore-test subnet', result.stderr)
        self.assertFalse(self.target.exists())


if __name__ == '__main__':
    unittest.main()
