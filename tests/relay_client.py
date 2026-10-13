"""A small client of the relay's HTTP interface for the tests: sign-in by cookie, JSON in and out, one kept connection."""
import http.client
import json
import secrets
import time
from urllib.parse import urlsplit


class Client:
    def __init__(self, origin, timeout=10):
        self.url = urlsplit(origin)
        self.timeout = timeout
        self.cookie = {}
        self.connection = None

    def connect(self):
        if self.connection is None:
            cls = http.client.HTTPSConnection if self.url.scheme == "https" else http.client.HTTPConnection
            self.connection = cls(self.url.hostname, self.url.port, timeout=self.timeout)
        return self.connection

    def call(self, path, payload=None):
        body = None if payload is None else json.dumps(payload).encode("utf-8")
        headers = dict(self.cookie)
        if body is not None:
            headers["Content-Type"] = "application/json"
        for attempt in range(2):
            try:
                self.connect().request("GET" if body is None else "POST", path, body, headers)
                reply = self.connection.getresponse()
                data = reply.read()
                break
            except (http.client.HTTPException, OSError):
                self.close()
                if attempt:
                    raise
        if reply.getheader("Set-Cookie"):
            self.cookie["Cookie"] = reply.getheader("Set-Cookie").split(";", 1)[0]
        if reply.status != 200:
            raise RuntimeError("%s returned HTTP %d" % (path.split("?", 1)[0], reply.status))
        return json.loads(data)

    def close(self):
        if self.connection is not None:
            self.connection.close()
            self.connection = None


def operation(terminal, action, **fields):
    return dict(fields, id=secrets.token_hex(16), terminal=terminal, action=action)


def await_ready(check, description, seconds=45):
    deadline = time.monotonic() + seconds
    last = None
    while time.monotonic() < deadline:
        try:
            result = check()
            if result:
                return result
        except (OSError, http.client.HTTPException, RuntimeError) as error:
            last = type(error).__name__
        time.sleep(0.2)
    raise TimeoutError("%s not ready%s" % (description, " (%s)" % last if last else ""))
