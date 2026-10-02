"""Exercise the built browser route, not the source loader or a development server."""

import os
import re
from pathlib import Path
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from threading import Thread
import unittest

from playwright.sync_api import expect, sync_playwright


OUTPUT = Path(os.environ.get("WEBSITE_BUILD_DIR", "build/client")).resolve()
BASE = "/" + os.environ.get("VITE_BASE", "/").strip("/")
BASE = BASE.rstrip("/") + "/"


class StaticHandler(SimpleHTTPRequestHandler):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=str(OUTPUT), **kwargs)

    def translate_path(self, path):
        candidate = Path(super().translate_path(path))
        # Production prerenders are nested under the base; Vite assets are not.
        if not candidate.exists() and BASE != "/" and path.startswith(BASE):
            return super().translate_path("/" + path[len(BASE):])
        return str(candidate)

    def log_message(self, format, *args):
        pass

    def copyfile(self, source, outputfile):
        try:
            super().copyfile(source, outputfile)
        except (BrokenPipeError, ConnectionResetError, ConnectionAbortedError):
            print(f"Browser disconnected while streaming {self.path}", flush=True)


class TutorialNavigationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if not (OUTPUT / BASE.strip("/") / "index.html").exists():
            raise RuntimeError(f"Build the site for {BASE} first: {OUTPUT}")
        cls.server = ThreadingHTTPServer(("127.0.0.1", 0), StaticHandler)
        cls.server_thread = Thread(target=cls.server.serve_forever, daemon=True)
        cls.server_thread.start()
        cls.origin = f"http://127.0.0.1:{cls.server.server_port}"
        cls.playwright = sync_playwright().start()
        cls.browser = cls.playwright.chromium.launch()

    @classmethod
    def tearDownClass(cls):
        cls.browser.close()
        cls.playwright.stop()
        cls.server.shutdown()
        cls.server.server_close()
        cls.server_thread.join()

    def setUp(self):
        self.context = self.browser.new_context(viewport={"width": 1440, "height": 1000})
        self.addCleanup(self.context.close)
        # The star badge is not part of navigation and must not require GitHub access.
        self.context.route("https://api.github.com/**", lambda route: route.fulfill(
            json={"stargazers_count": 0}))
        self.page = self.context.new_page()
        self.route_errors = []

        def record_error(message):
            print(f"Browser error: {message}", flush=True)
            if re.search(r"ReferenceError|/assets/Tutorial-[^\s]+\.js|route module|dynamically imported module", message):
                self.route_errors.append(message)

        self.page.on("pageerror", lambda error: record_error(error.stack))
        self.page.on("console", lambda message: record_error(message.text) if message.type == "error" else None)
        self.page.goto(self.origin + BASE)
        self.page.get_by_role("button", name="Docs", exact=True).click()
        expect(self.page.locator("#desktop-docs-navigation")).to_be_visible()

    def assert_chapter(self, slug, title):
        expect(self.page).to_have_url(self.origin + BASE + "docs/tutorial/" + slug + "/")
        expect(self.page.get_by_role("heading", level=1)).to_have_text(title)

    def test_compiled_client_loader_executes_valid_redirect_and_not_found_branches(self):
        modules = list((OUTPUT / "assets").glob("Tutorial-*.js"))
        self.assertEqual(len(modules), 1, "Expected the real split Tutorial browser module")
        result = self.page.evaluate("""async (moduleUrl) => {
            const route = await import(moduleUrl);
            const args = (params) => ({
                params, request: new Request(location.href),
                serverLoader() { throw new Error("Unexpected serverLoader call"); }
            });
            const valid = await route.clientLoader(args({ chapterId: "hello-world" }));
            const missing = await route.clientLoader(args({}));
            let invalid;
            try {
                await route.clientLoader(args({ chapterId: "not-a-chapter" }));
                throw new Error("Invalid chapter was accepted");
            } catch (error) {
                if (!(error instanceof Response)) throw error;
                invalid = error.status;
            }
            return { valid, missing: missing.status,
                destination: missing.headers.get("Location"), invalid };
        }""", self.origin + BASE + "assets/" + modules[0].name)
        self.assertEqual(result, {
            "valid": None, "missing": 302,
            "destination": "/docs/tutorial/hello-world/", "invalid": 404,
        })
        self.assertEqual(self.route_errors, [])

    def test_home_entries_chapters_and_history_are_client_navigations(self):
        for entry in ["Docs", "Quick Start", "FAQ"]:
            with self.subTest(entry=entry):
                self.page.goto(self.origin + BASE)
                docs = self.page.get_by_role("button", name="Docs", exact=True)
                docs.click()
                expect(self.page.locator("#desktop-docs-navigation")).to_be_visible()
                self.page.evaluate("window.__tutorialNavigationSentinel = 'same-document'")
                documents = []
                on_request = lambda request: documents.append(request.url) if request.resource_type == "document" else None
                self.page.on("request", on_request)
                try:
                    if entry == "Docs":
                        self.page.locator("#desktop-docs-navigation").get_by_role(
                            "link", name="Tutorial Build your first app").click()
                    else:
                        docs.click()
                        if entry == "Quick Start":
                            self.page.get_by_role("link", name="Start the tutorial", exact=True).click()
                        else:
                            faq = self.page.locator('section[aria-labelledby="home-faq-title"]')
                            faq.get_by_text("How do I get started?", exact=True).click()
                            faq.get_by_role("link", name="interactive tutorial", exact=True).click()
                    self.assert_chapter("hello-world", "Chapter 1 \u00b7 Hello World")
                    terminal = self.page.locator(".xterm")
                    expect(terminal).to_contain_text(
                        "Try the focused button below.", use_inner_text=True, timeout=30000)
                    terminal.locator("textarea").press("Enter")
                    expect(terminal).to_contain_text(
                        "Button pressed 1 time.", use_inner_text=True)
                    self.page.get_by_role("button", name="Restart preview", exact=True).click()
                    expect(terminal).to_contain_text(
                        "Try the focused button below.", use_inner_text=True, timeout=30000)
                    self.page.get_by_role("navigation", name="Adjacent tutorial chapters").get_by_role(
                        "link", name="Chapter 2", exact=True).click()
                    self.assert_chapter("state-and-events", "Chapter 2 \u00b7 State and Events")
                    self.page.go_back()
                    self.assert_chapter("hello-world", "Chapter 1 \u00b7 Hello World")
                    self.page.go_forward()
                    self.assert_chapter("state-and-events", "Chapter 2 \u00b7 State and Events")
                    self.page.go_back()
                    self.page.go_back()
                    expect(self.page).to_have_url(self.origin + BASE)
                    expect(self.page.get_by_role("heading", level=1)).to_have_text(
                        "Build TUI with Razor Component")
                    self.assertEqual(self.page.evaluate("window.__tutorialNavigationSentinel"), "same-document")
                    self.assertEqual(documents, [], "Navigation must not fall back to reloading a document")
                    self.assertEqual(self.route_errors, [])
                finally:
                    self.page.remove_listener("request", on_request)

    def test_missing_chapter_redirect_keeps_the_deployment_base(self):
        self.page.goto(self.origin + BASE + "docs/tutorial/")
        self.assert_chapter("hello-world", "Chapter 1 \u00b7 Hello World")
        self.assertEqual(self.route_errors, [])


if __name__ == "__main__":
    unittest.main(verbosity=2)
