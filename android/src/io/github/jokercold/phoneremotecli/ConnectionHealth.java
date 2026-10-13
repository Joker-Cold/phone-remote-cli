package io.github.jokercold.phoneremotecli;

/**
 * When the app gives up on a computer and returns to pairing. Failed checks are waited out for about half a minute:
 * the program on the computer restarts its service when the Tailscale address changes, and its pages carry on by
 * themselves afterwards.
 */
final class ConnectionHealth {
    static final int PATIENCE = 10;      // checks three seconds apart, each waiting up to three more
    private int failures;
    private boolean signedIn;
    /** signedIn: the page was opened on an earlier sign-in, not with a password it still has to try. */
    ConnectionHealth(boolean signedIn) { this.signedIn = signedIn; }
    boolean sample(boolean reachable) {
        if (reachable) { failures = 0; return false; }
        return ++failures >= PATIENCE;
    }
    /**
     * Whether the computer no longer knows this phone (its password was changed, or the phone signed out), so the
     * app goes back to scanning instead of leaving the page's password form as the only way on. A password that is
     * still being tried, or was wrong, stays with the form.
     */
    boolean signedOut(boolean signedIn) {
        if (signedIn) { this.signedIn = true; return false; }
        return this.signedIn;
    }
    /** A page that fails with a server error is given up at once; the pages retry their own requests. */
    static boolean httpError(boolean mainFrame, int status) {
        return status >= 500 && mainFrame;
    }
}
