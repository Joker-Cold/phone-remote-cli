package io.github.jokercold.phoneremotecli;

/** Regression cases for transient failures, page errors and unrelated WebView resources. */
public final class ConnectionHealthCheck {
    private static void expect(boolean value, String name) {
        if (!value) throw new AssertionError(name);
    }
    public static void main(String[] args) {
        expect(ConnectionHealth.httpError(true, 502), "a page that fails with a server error returns to pairing");
        expect(!ConnectionHealth.httpError(false, 502), "the pages retry their own requests to a restarting computer");
        expect(!ConnectionHealth.httpError(false, 401), "expired login remains on login page");
        expect(!ConnectionHealth.httpError(true, 404), "missing page is not a connection failure");
        ConnectionHealth earlier = new ConnectionHealth(true);
        expect(!earlier.signedOut(true), "a valid earlier sign-in stays on the page");
        expect(earlier.signedOut(false), "an expired earlier sign-in returns to scanning");
        ConnectionHealth typed = new ConnectionHealth(false);
        expect(!typed.signedOut(false), "a password still being tried stays on the page");
        expect(!typed.signedOut(true), "a password that worked stays on the page");
        expect(typed.signedOut(false), "a sign-in lost later returns to scanning");
        ConnectionHealth health = new ConnectionHealth(true);
        expect(!health.sample(false), "one timeout is retried");
        expect(!health.sample(true), "healthy response resets failures");
        expect(!health.sample(false), "timeout after recovery is retried");
        for (int n = 2; n < ConnectionHealth.PATIENCE; n++) expect(!health.sample(false), "a restart of the computer's service is waited out");
        expect(health.sample(false), "a computer that stays away returns to pairing");
        System.out.println("Connection health: failure recovery, login, lost sign-ins and resource isolation passed");
    }
}
