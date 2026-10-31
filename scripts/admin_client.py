"""Cookie + CSRF client. Passwords are prompted or read from an explicit private file, never arguments."""
import getpass
import http.cookiejar
import json
import os
import stat
import urllib.parse
import urllib.request


def validate_base(base):
    url = urllib.parse.urlsplit(base)
    if (url.username or url.password or url.query or url.fragment or not url.hostname
            or url.path.rstrip('/') != '/api'):
        raise ValueError('Use an explicit origin ending in /api, without credentials, query, or fragment.')
    local = url.hostname in ('localhost', '127.0.0.1', '::1')
    if url.scheme != 'https' and not (local and url.scheme == 'http'):
        raise ValueError('Hosted operations require an explicit HTTPS --base target; HTTP is loopback-only.')
    return base.rstrip('/')


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        raise ValueError('API redirect rejected. Use the final HTTPS origin explicitly.')


class AdminClient:
    def __init__(self, base, password_file=None):
        self.base = validate_base(base)
        self.opener = urllib.request.build_opener(NoRedirect(), urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
        self.token = None
        self.authenticated = False
        self.password_file = password_file

    def _request(self, path, method='GET', body=None):
        if not path.startswith('/') or path.startswith('//'):
            raise ValueError('API paths must be relative to the selected server.')
        data = None if body is None else json.dumps(body).encode()
        headers = {'Accept': 'application/json', 'Content-Type': 'application/json'}
        if method not in ('GET', 'HEAD', 'OPTIONS') and self.token:
            headers['X-CSRF-TOKEN'] = self.token
        req = urllib.request.Request(self.base + path, data=data, method=method, headers=headers)
        with self.opener.open(req, timeout=90) as response:
            return None if response.status == 204 else json.load(response)

    def login(self):
        password = read_password_file(self.password_file) if self.password_file else getpass.getpass(f'Administrator password for {self.base}: ')
        self.token = self._request('/auth/csrf')['token']
        try:
            self._request('/auth/login', 'POST', {'password': password})
        finally:
            password = None
        self.token = self._request('/auth/csrf')['token']
        self.authenticated = True

    def logout(self):
        try:
            if self.authenticated:
                self._request('/auth/logout', 'POST')
        finally:
            self.authenticated = False
            self.token = None

    def request(self, path, method='GET', body=None):
        if not self.authenticated:
            self.login()
        return self._request(path, method, body)


_clients = {}


def read_password_file(path):
    """Explicit CI input: bounded, regular, owner-only file; no ambient env fallback."""
    if os.path.islink(path):
        raise ValueError('The password file must not be a symlink.')
    descriptor = os.open(path, os.O_RDONLY | getattr(os, 'O_NOFOLLOW', 0) | getattr(os, 'O_NONBLOCK', 0))
    with os.fdopen(descriptor, 'rb') as stream:
        info = os.fstat(stream.fileno())
        if not stat.S_ISREG(info.st_mode):
            raise ValueError('The password file must be a regular file.')
        if os.name == 'posix' and (stat.S_IMODE(info.st_mode) != 0o600 or info.st_uid != os.geteuid()):
            raise ValueError('The password file must be owned by the current user with permissions 600.')
        value = stream.read(4097)
    if not value or len(value) > 4096 or any(character in value for character in (b'\n', b'\r', b'\0')):
        raise ValueError('The password file must contain one nonempty password, without a trailing newline (maximum 4096 bytes).')
    return value.decode('utf-8')


def use_password_file(base, path):
    base = validate_base(base)
    _clients[base] = AdminClient(base, password_file=path)


def logout(base):
    client = _clients.pop(validate_base(base), None)
    if client:
        client.logout()


def request(base, path, method='GET', body=None):
    base = validate_base(base)
    if base not in _clients:
        _clients[base] = AdminClient(base)
    return _clients[base].request(path, method, body)
