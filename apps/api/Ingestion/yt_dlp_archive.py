"""Bounded, metadata-only uploads listing. Run with isolated Python (-I).

Pinned API reviewed against yt-dlp 2026.10.26 YoutubeDL.py and youtube/_tab.py.
The uploads playlist mapping is covered by that extractor's own fixtures. No CLI
configuration, plugins, browser state, media, captions or approximate dates are used.
"""

import argparse
import contextlib
import datetime
import itertools
import json
import os
import re
import sys
from urllib.parse import urlsplit

SCHEMA_VERSION = 1
PINNED_VERSION = "2026.10.26"
MAX_REQUESTS = 32
MAX_RESPONSE_BYTES = 8_000_000
MAX_TOTAL_BYTES = 24_000_000
MAX_OUTPUT_BYTES = 500_000
CHANNEL_RE = re.compile(r"UC[A-Za-z0-9_-]{22}\Z")
VIDEO_RE = re.compile(r"[A-Za-z0-9_-]{11}\Z")
NETWORK_HOSTS = frozenset(("www.youtube.com", "youtube.com", "m.youtube.com", "youtubei.googleapis.com"))


class ArchiveError(Exception):
    def __init__(self, code):
        super().__init__(code)
        self.code = code


class SilentOutput:
    """Do not retain or echo provider diagnostics, which may contain private data."""

    def write(self, value):
        return len(value)

    def flush(self):
        pass

    def isatty(self):
        return False


def classify_error(error):
    if isinstance(error, ArchiveError):
        return error.code
    text = str(error).lower()
    if re.search(r"(?:http(?: error)?\s*:?\s*429|too many requests|rate.limit)", text):
        return "rate-limited"
    if any(word in text for word in ("sign in", "sign-in", "not a bot", "captcha", "403", "401", "age-restricted", "login required", "members-only", "private playlist")):
        return "blocked"
    if any(word in text for word in ("timed out", "timeout", "network", "connection", "resolve", "http error 5", "http 5")):
        return "network"
    return "extractor"


class SafeLogger:
    def __init__(self):
        self.warnings = set()
        self.failure = None

    def debug(self, _message):
        pass

    def warning(self, message):
        text = str(message).lower()
        # The extractor can report hidden/private slots without failing the whole
        # public playlist. All other warnings may indicate incomplete extraction.
        if "unavailable video" in text and not any(word in text for word in ("unable", "failed", "incomplete")):
            self.warnings.add("unavailable-entries")
        else:
            self.failure = classify_error(message)

    def error(self, message):
        self.failure = classify_error(message)

    def check(self):
        if self.failure:
            raise ArchiveError(self.failure)


class BoundedResponse:
    def __init__(self, response, budget):
        self.response = response
        self.budget = budget
        self.read_bytes = 0

    def __getattr__(self, name):
        return getattr(self.response, name)

    def read(self, size=-1):
        remaining = min(MAX_RESPONSE_BYTES - self.read_bytes, MAX_TOTAL_BYTES - self.budget.response_bytes)
        value = self.response.read(min(size, remaining + 1) if size is not None and size >= 0 else remaining + 1)
        self.read_bytes += len(value)
        self.budget.response_bytes += len(value)
        if self.read_bytes > MAX_RESPONSE_BYTES or self.budget.response_bytes > MAX_TOTAL_BYTES:
            raise ArchiveError("request-limit")
        return value

    def __enter__(self):
        return self

    def __exit__(self, *_args):
        self.response.close()


def bounded_downloader(base):
    class MetadataOnlyDownloader(base):
        request_count = 0
        response_bytes = 0

        def urlopen(self, request):
            self.request_count += 1
            if self.request_count > MAX_REQUESTS:
                raise ArchiveError("request-limit")
            raw = request if isinstance(request, str) else getattr(request, "url", None)
            parsed = urlsplit(raw or "")
            if parsed.scheme != "https" or parsed.hostname not in NETWORK_HOSTS or parsed.port not in (None, 443) or parsed.username or parsed.password:
                raise ArchiveError("blocked")
            return BoundedResponse(super().urlopen(request), self)

    return MetadataOnlyDownloader


def clean_text(value, limit):
    if not isinstance(value, str):
        return None
    value = "".join(char for char in value if char >= " " or char in "\n\t").strip()
    # Match the C# consumer's UTF-16 character limits, including non-BMP titles.
    units = 0
    end = 0
    for index, char in enumerate(value):
        units += 2 if ord(char) > 0xFFFF else 1
        if units > limit:
            break
        end = index + 1
    return value[:end] or None


def exact_upload_date(value):
    if not isinstance(value, str) or not re.fullmatch(r"[0-9]{8}", value):
        return None
    try:
        datetime.date(int(value[:4]), int(value[4:6]), int(value[6:]))
    except ValueError:
        return None
    return value


def thumbnail(entry):
    choices = [entry.get("thumbnail")]
    thumbs = entry.get("thumbnails")
    if isinstance(thumbs, (list, tuple)):
        choices.extend(t.get("url") for t in thumbs[:20] if isinstance(t, dict))
    for value in choices:
        if not isinstance(value, str) or len(value) > 2048:
            continue
        try:
            url = urlsplit(value)
            if url.scheme == "https" and url.hostname in ("i.ytimg.com", "i9.ytimg.com", "img.youtube.com") and url.port in (None, 443) and not url.username and not url.password:
                return value
        except ValueError:
            pass
    return None


def normalize_entry(entry, index, channel_id, logger, verified_playlist_owner=None):
    if entry is None:
        logger.warnings.add("entries-skipped")
        return None
    if not isinstance(entry, dict) or entry.get("_type") in ("playlist", "multi_video"):
        raise ArchiveError("invalid-output")
    video_id = entry.get("id")
    title = clean_text(entry.get("title"), 1000)
    if not isinstance(video_id, str) or not VIDEO_RE.fullmatch(video_id) or not title:
        logger.warnings.add("entries-skipped")
        return None
    owner = entry.get("channel_id") or verified_playlist_owner
    if owner is None:
        logger.warnings.add("ownership-missing")
        return None
    if owner != channel_id:
        logger.warnings.add("ownership-mismatch-skipped")
        return None
    availability = entry.get("availability")
    if availability is not None and (not isinstance(availability, str) or not re.fullmatch(r"[a-z_\-]{1,40}", availability)):
        raise ArchiveError("invalid-output")
    if availability in ("private", "premium_only", "subscriber_only", "needs_auth") or title.lower() in ("[private video]", "[deleted video]", "private video", "deleted video"):
        logger.warnings.add("unavailable-entries")
        return None
    return {
        "id": video_id,
        "title": title,
        "channelId": owner,
        "author": clean_text(entry.get("channel") or entry.get("uploader"), 400),
        "description": clean_text(entry.get("description"), 5000),
        "thumbnail": thumbnail(entry),
        "uploadDate": exact_upload_date(entry.get("upload_date")),
        "availability": availability,
        "isLive": entry.get("is_live") is True or entry.get("live_status") == "is_live",
        "isUpcoming": entry.get("live_status") == "is_upcoming",
        "index": index,
    }


def extract_page(channel_id, start, limit, module):
    if not CHANNEL_RE.fullmatch(channel_id) or not 1 <= start <= 200_000 or not 1 <= limit <= 50:
        raise ArchiveError("invalid-output")
    logger = SafeLogger()
    options = {
        "quiet": True, "no_warnings": False, "logger": logger,
        "skip_download": True, "simulate": True, "extract_flat": True,
        "lazy_playlist": True, "ignoreerrors": False,
        "socket_timeout": 10, "retries": 0, "extractor_retries": 0,
        "fragment_retries": 0, "file_access_retries": 0,
        "cachedir": False, "proxy": "", "geo_bypass": False,
        "cookiefile": None, "cookiesfrombrowser": None, "usenetrc": False,
        "username": None, "password": None, "netrc_cmd": None,
        "js_runtimes": {}, "remote_components": [],
        "writesubtitles": False, "writeautomaticsub": False,
        "writethumbnail": False, "writeinfojson": False,
        "writedescription": False, "allow_playlist_files": False,
        "getcomments": False, "postprocessors": [], "progress_hooks": [],
        "warn_when_outdated": False, "color": "never",
        "allowed_extractors": ["youtube:tab"],
        "extractor_args": {"youtubetab": {"approximate_date": []}},
    }
    playlist_id = "UU" + channel_id[2:]
    url = "https://www.youtube.com/playlist?list=" + playlist_id
    with bounded_downloader(module.YoutubeDL)(options) as downloader:
        # process=False preserves raw lazy slots and never resolves a video into
        # media formats, JS, captions or another extractor. islice consumes only
        # the requested window and one lookahead, even for a lazy playlist.
        result = downloader.extract_info(url, download=False, process=False, ie_key="YoutubeTab")
        logger.check()
        if not isinstance(result, dict) or result.get("_type") not in ("playlist", "multi_video") or result.get("id") != playlist_id:
            raise ArchiveError("invalid-output")
        owner = result.get("channel_id")
        if owner is not None and owner != channel_id:
            raise ArchiveError("invalid-output")
        raw_entries = result.get("entries")
        if raw_entries is None or isinstance(raw_entries, (str, bytes, dict)):
            raise ArchiveError("invalid-output")
        window = iter(itertools.islice(iter(raw_entries), start - 1, start + limit))
        output = []
        consumed = 0
        exhausted = False
        for position in range(limit + 1):
            try:
                raw = next(window)
            except StopIteration:
                exhausted = True
                break
            logger.check()
            if position == limit:
                break  # Lookahead is deliberately not consumed by nextStart.
            item = normalize_entry(raw, start + position, channel_id, logger, owner)
            consumed += 1
            if item is not None:
                output.append(item)
        logger.check()
    return {"schemaVersion": SCHEMA_VERSION, "version": PINNED_VERSION, "channelId": channel_id,
            "start": start, "nextStart": start + consumed, "exhausted": exhausted,
            "entries": output, "warnings": sorted(logger.warnings)[:20]}


class JsonArgumentParser(argparse.ArgumentParser):
    def error(self, _message):
        raise ArchiveError("invalid-output")


def main(argv=None):
    version = None
    try:
        parser = JsonArgumentParser(add_help=False, allow_abbrev=False)
        parser.add_argument("--channel", required=True)
        parser.add_argument("--start", type=int, required=True)
        parser.add_argument("--limit", type=int, required=True)
        args = parser.parse_args(argv)
        if not CHANNEL_RE.fullmatch(args.channel) or not 1 <= args.start <= 200_000 or not 1 <= args.limit <= 50:
            raise ArchiveError("invalid-output")
        os.environ["YTDLP_NO_PLUGINS"] = "1"
        with contextlib.redirect_stdout(SilentOutput()), contextlib.redirect_stderr(SilentOutput()):
            try:
                import yt_dlp
                from yt_dlp import version as version_module
                from yt_dlp.globals import plugin_dirs
            except ImportError:
                raise ArchiveError("dependency-missing") from None
            plugin_dirs.value = []
            version = version_module.__version__
            if version != PINNED_VERSION:
                raise ArchiveError("version-mismatch")
            payload = extract_page(args.channel, args.start, args.limit, yt_dlp)
        encoded = json.dumps(payload, ensure_ascii=False, separators=(",", ":"))
        if len(encoded.encode("utf-8")) + 1 > MAX_OUTPUT_BYTES:
            raise ArchiveError("invalid-output")
        print(encoded)
        return 0
    except Exception as error:
        payload = {"schemaVersion": SCHEMA_VERSION, "version": "unavailable", "errorCode": classify_error(error)}
        if isinstance(version, str) and re.fullmatch(r"[0-9][A-Za-z0-9.+_\-]{1,63}", version):
            payload["version"] = version
        print(json.dumps(payload, separators=(",", ":")))
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
