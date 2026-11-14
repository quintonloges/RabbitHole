"""Offline protocol/safety fixtures. The fake library never opens a socket."""

import contextlib
import importlib.util
import io
import json
from pathlib import Path
import sys
import types
import unittest
from unittest.mock import patch


SCRIPT = Path(__file__).resolve().parents[2] / "apps/api/Ingestion/yt_dlp_archive.py"
SPEC = importlib.util.spec_from_file_location("rabbit_yt_dlp_archive", SCRIPT)
helper = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(helper)
CHANNEL = "UC" + "A" * 22
PLAYLIST = "UU" + CHANNEL[2:]


def entry(number, **extra):
    return {"id": f"v{number:010d}", "title": f"Original video {number}", "channel_id": CHANNEL, **extra}


class FakeDownloader:
    result_factory = None
    options = None
    request = None

    def __init__(self, options):
        FakeDownloader.options = options

    def __enter__(self):
        return self

    def __exit__(self, *_args):
        pass

    def extract_info(self, url, **kwargs):
        FakeDownloader.request = (url, kwargs)
        return FakeDownloader.result_factory()


def fake_modules(result):
    FakeDownloader.result_factory = staticmethod(result)
    module = types.ModuleType("yt_dlp")
    module.YoutubeDL = FakeDownloader
    version = types.ModuleType("yt_dlp.version")
    version.__version__ = helper.PINNED_VERSION
    globals_module = types.ModuleType("yt_dlp.globals")
    globals_module.plugin_dirs = types.SimpleNamespace(value=["default"])
    module.version = version
    return {"yt_dlp": module, "yt_dlp.version": version, "yt_dlp.globals": globals_module}


class YtDlpArchiveTests(unittest.TestCase):
    def page(self, entries, start=1, limit=2, **extra):
        module = fake_modules(lambda: {"_type": "playlist", "id": PLAYLIST, "channel_id": CHANNEL, "entries": iter(entries), **extra})["yt_dlp"]
        return helper.extract_page(CHANNEL, start, limit, module)

    def run_main(self, modules, args=None):
        stdout = io.StringIO()
        stderr = io.StringIO()
        with patch.dict(sys.modules, modules), contextlib.redirect_stdout(stdout), contextlib.redirect_stderr(stderr):
            code = helper.main(args or ["--channel", CHANNEL, "--start", "1", "--limit", "2"])
        self.assertEqual("", stderr.getvalue())
        return code, json.loads(stdout.getvalue()), stdout.getvalue()

    def test_raw_positions_and_lookahead_do_not_drop_candidates(self):
        consumed = []

        def raw():
            for number in range(5):
                consumed.append(number)
                yield None if number == 0 else entry(number)

        first = self.page(raw())
        self.assertEqual([0, 1, 2], consumed)
        self.assertEqual(3, first["nextStart"])
        self.assertFalse(first["exhausted"])
        self.assertEqual([2], [item["index"] for item in first["entries"]])
        second = self.page([None, entry(1), entry(2), entry(3), entry(4)], start=3)
        self.assertEqual([entry(2)["id"], entry(3)["id"]], [item["id"] for item in second["entries"]])
        self.assertEqual(5, second["nextStart"])
        last = self.page([None, entry(1), entry(2), entry(3), entry(4)], start=5)
        self.assertTrue(last["exhausted"])
        self.assertEqual(6, last["nextStart"])

    def test_empty_and_exact_sized_end_are_honestly_exhausted(self):
        empty = self.page([], start=10)
        self.assertEqual(10, empty["nextStart"])
        self.assertTrue(empty["exhausted"])
        exact = self.page([entry(1), entry(2)])
        self.assertTrue(exact["exhausted"])
        self.assertEqual(3, exact["nextStart"])

    def test_only_allowlisted_bounded_metadata_and_exact_dates_are_returned(self):
        original = entry(1, title="T" * 2000, channel="A" * 800, description="D" * 10000,
                         upload_date="20260228", timestamp=12345, webpage_url="https://private.example/secret",
                         formats=[{"url": "https://private.example/media"}], subtitles={"en": "secret"},
                         thumbnails=[{"url": "https://evil.example/tracker"}, {"url": "https://i.ytimg.com/vi/test/hqdefault.jpg"}],
                         live_status="is_upcoming")
        output = self.page([original])["entries"][0]
        self.assertEqual(1000, len(output["title"]))
        self.assertEqual(400, len(output["author"]))
        self.assertEqual(5000, len(output["description"]))
        self.assertEqual("20260228", output["uploadDate"])
        self.assertTrue(output["isUpcoming"])
        self.assertEqual("https://i.ytimg.com/vi/test/hqdefault.jpg", output["thumbnail"])
        self.assertNotIn("formats", output)
        self.assertNotIn("subtitles", output)
        self.assertNotIn("private.example", json.dumps(output))
        self.assertEqual(500, len(helper.clean_text("🌒" * 1000, 1000)))
        for value in ("20260230", "2 days ago", "2026-02-28", None, 20260228):
            with self.subTest(value=value):
                self.assertIsNone(helper.exact_upload_date(value))

    def test_private_missing_and_other_channel_slots_advance_without_import(self):
        output = self.page([entry(1, availability="private"), {"id": "bad", "title": "Not a video"},
                            entry(3, channel_id="UC" + "B" * 22), entry(4)], limit=4)
        self.assertEqual(5, output["nextStart"])
        self.assertEqual([entry(4)["id"]], [item["id"] for item in output["entries"]])
        self.assertIn("unavailable-entries", output["warnings"])
        self.assertIn("ownership-mismatch-skipped", output["warnings"])

    def test_playlist_ownership_and_nested_playlist_output_are_validated(self):
        for extra in ({"id": "UU" + "B" * 22}, {"channel_id": "UC" + "B" * 22}):
            with self.subTest(extra=extra), self.assertRaises(helper.ArchiveError):
                self.page([entry(1)], **extra)
        with self.assertRaises(helper.ArchiveError):
            self.page([{"_type": "playlist", "id": PLAYLIST}])

    def test_verified_playlist_owner_fills_missing_entry_owner_but_absence_is_explicit(self):
        verified = self.page([entry(1, channel_id=None)])
        self.assertEqual(CHANNEL, verified["entries"][0]["channelId"])
        missing = self.page([entry(1, channel_id=None)], channel_id=None)
        self.assertEqual([], missing["entries"])
        self.assertEqual(["ownership-missing"], missing["warnings"])
        self.assertEqual(2, missing["nextStart"])

    def test_library_is_metadata_only_and_does_not_read_configuration(self):
        self.page([entry(1)])
        url, kwargs = FakeDownloader.request
        self.assertEqual("https://www.youtube.com/playlist?list=" + PLAYLIST, url)
        self.assertEqual({"download": False, "process": False, "ie_key": "YoutubeTab"}, kwargs)
        options = FakeDownloader.options
        for name in ("simulate", "skip_download", "extract_flat", "lazy_playlist"):
            self.assertTrue(options[name])
        for name in ("cachedir", "geo_bypass", "usenetrc", "writesubtitles", "writeautomaticsub", "writeinfojson", "writethumbnail", "getcomments"):
            self.assertFalse(options[name])
        self.assertEqual({}, options["js_runtimes"])
        self.assertEqual([], options["remote_components"])
        self.assertEqual("", options["proxy"])
        self.assertIsNone(options["cookiefile"])
        self.assertIsNone(options["cookiesfrombrowser"])
        self.assertEqual(0, options["retries"])
        self.assertEqual(0, options["extractor_retries"])
        self.assertEqual(10, options["socket_timeout"])
        self.assertEqual([], options["extractor_args"]["youtubetab"]["approximate_date"])

    def test_main_json_schema_disables_plugins_and_suppresses_provider_output(self):
        def result():
            print("provider secret output")
            print("provider secret error", file=sys.stderr)
            return {"_type": "playlist", "id": PLAYLIST, "channel_id": CHANNEL, "entries": [entry(1)]}
        modules = fake_modules(result)
        code, output, text = self.run_main(modules)
        self.assertEqual(0, code)
        self.assertEqual(1, output["schemaVersion"])
        self.assertEqual(helper.PINNED_VERSION, output["version"])
        self.assertNotIn("secret", text)
        self.assertEqual([], modules["yt_dlp.globals"].plugin_dirs.value)

    def test_invalid_arguments_never_reach_library_or_echo_private_input(self):
        for arguments in (["--channel", "https://private.example/secret", "--start", "1", "--limit", "1"],
                          ["--channel", CHANNEL, "--start", "0", "--limit", "1"],
                          ["--channel", CHANNEL, "--start", "200001", "--limit", "1"],
                          ["--channel", CHANNEL, "--start", "1", "--limit", "51"], ["--unknown", "secret"]):
            with self.subTest(arguments=arguments):
                code, output, text = self.run_main({}, arguments)
                self.assertEqual(1, code)
                self.assertEqual("invalid-output", output["errorCode"])
                self.assertNotIn("secret", text)

    def test_missing_or_mismatched_dependency_is_explicit(self):
        code, output, _ = self.run_main({"yt_dlp": None})
        self.assertEqual(1, code)
        self.assertEqual("dependency-missing", output["errorCode"])
        self.assertEqual("unavailable", output["version"])
        modules = fake_modules(lambda: None)
        modules["yt_dlp.version"].__version__ = "2025.01.01"
        code, output, _ = self.run_main(modules)
        self.assertEqual(1, code)
        self.assertEqual("version-mismatch", output["errorCode"])
        self.assertEqual("2025.01.01", output["version"])

    def test_errors_are_classified_without_raw_provider_diagnostics(self):
        for message, expected in (("HTTP Error 429: private secret", "rate-limited"),
                                  ("Sign in to confirm you're not a bot secret", "blocked"),
                                  ("connection timed out secret", "network"), ("extractor failed secret", "extractor")):
            def fail(message=message):
                raise RuntimeError(message)
            code, output, text = self.run_main(fake_modules(fail))
            self.assertEqual(1, code)
            self.assertEqual(expected, output["errorCode"])
            self.assertNotIn("secret", text)

    def test_serious_incomplete_extraction_does_not_report_empty_success(self):
        def result():
            FakeDownloader.options["logger"].warning("Incomplete data received, secret provider response")
            return {"_type": "playlist", "id": PLAYLIST, "entries": []}
        code, output, text = self.run_main(fake_modules(result))
        self.assertEqual(1, code)
        self.assertEqual("extractor", output["errorCode"])
        self.assertNotIn("secret", text)

    def test_output_byte_limit_is_enforced(self):
        originals = [entry(i, description="🌒" * 5000) for i in range(50)]
        code, output, _ = self.run_main(fake_modules(lambda: {"_type": "playlist", "id": PLAYLIST, "entries": originals}),
                                        ["--channel", CHANNEL, "--start", "1", "--limit", "50"])
        self.assertEqual(1, code)
        self.assertEqual("invalid-output", output["errorCode"])

    def test_network_hosts_requests_and_response_bytes_are_bounded(self):
        class FakeNetwork:
            def urlopen(self, _request):
                return io.BytesIO(b"12345")
        downloader = helper.bounded_downloader(FakeNetwork)()
        for bad in ("http://www.youtube.com/", "https://127.0.0.1/", "https://www.youtube.com:444/", "https://user:secret@www.youtube.com/", "https://evil.example/"):
            with self.subTest(url=bad), self.assertRaises(helper.ArchiveError):
                downloader.urlopen(bad)
        downloader = helper.bounded_downloader(FakeNetwork)()
        for _ in range(helper.MAX_REQUESTS):
            downloader.urlopen("https://www.youtube.com/")
        with self.assertRaisesRegex(helper.ArchiveError, "request-limit"):
            downloader.urlopen("https://www.youtube.com/")
        with patch.object(helper, "MAX_RESPONSE_BYTES", 4):
            response = helper.bounded_downloader(FakeNetwork)().urlopen("https://www.youtube.com/")
            with self.assertRaisesRegex(helper.ArchiveError, "request-limit"):
                response.read()


if __name__ == "__main__":
    unittest.main()
