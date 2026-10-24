"""Cookie + CSRF client for maintenance tools. Passwords are prompted, never arguments."""
import getpass
import http.cookiejar
import json
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
    def __init__(self, base):
        self.base = validate_base(base)
        self.opener = urllib.request.build_opener(NoRedirect(), urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
        self.token = None
        self.authenticated = False

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
        self.token = self._request('/auth/csrf')['token']
        password = getpass.getpass(f'Administrator password for {self.base}: ')
        try:
            self._request('/auth/login', 'POST', {'password': password})
        finally:
            password = None
        self.token = self._request('/auth/csrf')['token']
        self.authenticated = True

    def request(self, path, method='GET', body=None):
        if not self.authenticated:
            self.login()
        return self._request(path, method, body)


_clients = {}


def request(base, path, method='GET', body=None):
    base = validate_base(base)
    if base not in _clients:
        _clients[base] = AdminClient(base)
    return _clients[base].request(path, method, body)
