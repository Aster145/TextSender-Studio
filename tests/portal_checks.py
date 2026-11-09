"""Offline portal browser checks. All HTTP requests are fulfilled with fixtures.

Requires Python 3.11+ and playwright. Run with --browser msedge on Windows,
or install Playwright Chromium and use the default --browser chromium.
"""
import argparse
import json
import re
import time
from pathlib import Path
from urllib.parse import parse_qs, urlparse
from playwright.sync_api import sync_playwright, expect

ROOT = Path(__file__).resolve().parents[1]
HEADER = ROOT / "firmware/TextSenderBridge/WifiPortalPage.h"
HTML = re.search(r'R"PORTAL\((.*)\)PORTAL";', HEADER.read_text(encoding="utf-8"), re.S).group(1)
OUTPUT = ROOT / "work/portal-checks"
OUTPUT.mkdir(parents=True, exist_ok=True)
checks = 0


def check(condition, message):
    global checks
    if not condition:
        raise AssertionError(message)
    checks += 1
    print("PASS:", message, flush=True)


class Portal:
    def __init__(self, browser, width=375):
        self.context = browser.new_context(viewport={"width": width, "height": 900})
        self.page = self.context.new_page()
        self.errors = []
        self.page.on("pageerror", lambda error: self.errors.append(str(error)))
        self.status = dict(configured=False, ssid="", station_connected=False,
                           station_ip="0.0.0.0", state="unconfigured", message="请选择 Wi-Fi。",
                           reason=0, ap_active=True, ap_shutdown_in_ms=0, retry_in_ms=0)
        self.networks = [dict(ssid="Lab Wi-Fi", rssi=-42, secure=True),
                         dict(ssid='Lab <b>&" Wi-Fi', rssi=-55, secure=True),
                         dict(ssid="Lab Guest", rssi=-65, secure=False)]
        self.scan_calls = 0
        self.scan_pending = 1
        self.posts = []
        self.finished = 0
        self.save_error = False
        self.status_offline = False
        self.joined_at = None
        self.page.add_init_script("""
            document.execCommand = function(command) {
              if (command === 'copy') { window.copiedValue = document.activeElement.value; return true; }
              return false;
            };
        """)
        self.page.route("**/*", self.route)

    def route(self, route):
        request = route.request
        path = urlparse(request.url).path
        code, body = 200, {}
        if path == "/":
            route.fulfill(status=200, content_type="text/html; charset=utf-8", body=HTML)
            return
        if path == "/wifi/status":
            if self.status_offline:
                route.abort("failed")
                return
            body = dict(self.status)
            if self.joined_at is not None:
                body["ap_shutdown_in_ms"] = max(0, 120000 - int((time.monotonic() - self.joined_at) * 1000))
        elif path == "/wifi/scan":
            self.scan_calls += 1
            if self.scan_pending > 0:
                self.scan_pending -= 1
                code, body = 202, dict(scanning=True)
            else:
                body = dict(scanning=False, networks=self.networks)
        elif path == "/wifi" and request.method == "POST":
            self.posts.append(parse_qs(request.post_data, keep_blank_values=True))
            if self.save_error:
                code, body = 500, dict(error="wifi_save_failed", message="测试：配置未能保存，请重试。")
            else:
                self.status.update(configured=True, ssid=self.posts[-1]["ssid"][0],
                                   state="connecting", station_connected=False,
                                   station_ip="0.0.0.0", message="正在获取 IP。")
                body = dict(saved=True, connecting=True)
        elif path == "/wifi/finish" and request.method == "POST":
            self.finished += 1
            body = dict(accepted=True)
        else:
            code, body = 404, dict(error="not_found")
        route.fulfill(status=code, content_type="application/json", body=json.dumps(body, ensure_ascii=False))

    def open(self):
        self.page.goto("http://portal.test/")
        self.page.wait_for_load_state("networkidle")
        expect(self.page.locator("#network option")).to_have_count(len(self.networks) + 1, timeout=6000)
        expect(self.page.locator("#network option").first).to_have_text(re.compile("请选择 Wi-Fi|未找到 Wi-Fi"), timeout=6000)

    def join(self, ssid="Lab Wi-Fi"):
        self.joined_at = time.monotonic()
        self.status.update(configured=True, ssid=ssid, station_connected=True,
                           station_ip="192.168.1.88", state="connected", message="已连接路由器。")

    def close(self):
        check(not self.errors, "no uncaught browser script errors")
        self.context.close()


def run(browser):
    portal = Portal(browser)
    page = portal.page
    portal.open()
    check(portal.scan_calls >= 2, "initial scan polls asynchronous results")
    check(page.locator("#network option").nth(2).text_content().startswith('Lab <b>&" Wi-Fi'),
          "SSID punctuation renders as plain text")
    check(page.locator("#network b").count() == 0, "SSID cannot insert markup into selection")
    page.locator("#connect").click()
    expect(page.locator("#noticeTitle")).to_have_text("请选择 Wi-Fi")
    check(not portal.posts, "missing SSID cannot submit")
    page.locator("#network").select_option("Lab Wi-Fi")
    page.locator("#password").fill("short")
    page.locator("#connect").click()
    check(not portal.posts, "short protected-network password cannot submit")
    page.locator("#password").fill("fixture-only-password")
    page.locator("#showPassword").check()
    check(page.locator("#password").get_attribute("type") == "text", "password visibility toggle works")
    page.screenshot(path=str(OUTPUT / "setup-375.png"), full_page=True)
    check(page.evaluate("document.documentElement.scrollWidth <= innerWidth"), "mobile setup fits viewport")
    page.locator("#showPassword").uncheck()
    page.locator("#connect").click()
    page.locator("#wifiForm").evaluate("form => form.dispatchEvent(new Event('submit', {bubbles:true,cancelable:true}))")
    expect(page.locator("#noticeTitle")).to_have_text("配置已保存")
    check(len(portal.posts) == 1 and portal.posts[0]["ssid"] == ["Lab Wi-Fi"], "save posts chosen SSID exactly once")
    check(page.url == "http://portal.test/", "save stays on portal instead of navigating to JSON")
    check(page.locator("#successPanel").is_hidden(), "saved response alone never means connected")
    check(page.locator("#password").input_value() == "", "submitted password is cleared from input")
    check(page.evaluate("localStorage.length === 0 && sessionStorage.length === 0"), "credentials are not stored in browser storage")
    portal.join()
    expect(page.locator("#successPanel")).to_be_visible(timeout=6500)
    expect(page.locator("#stationIp")).to_have_value("192.168.1.88")
    expect(page.locator("#manageUrl")).to_have_value("http://192.168.1.88/")
    check("秒" in page.locator("#countdown").text_content(), "successful connection displays AP countdown")
    check(page.evaluate("document.documentElement.scrollWidth <= innerWidth"), "mobile success fits viewport")
    page.locator("#copyIp").click()
    check(page.evaluate("window.copiedValue") == "192.168.1.88", "IP copy uses displayed address")
    page.locator("#copyUrl").click()
    check(page.evaluate("window.copiedValue") == "http://192.168.1.88/", "management URL copy uses device address")
    page.screenshot(path=str(OUTPUT / "success-375.png"), full_page=True)
    portal.status_offline = True
    expect(page.locator("#noticeTitle")).to_have_text("配网页面已断开", timeout=6500)
    check(page.locator("#stationIp").input_value() == "192.168.1.88", "IP remains visible after hotspot disconnects")
    portal.status_offline = False
    page.locator("#finish").click()
    expect(page.locator("#finish")).to_be_hidden()
    check(portal.finished == 1, "finish is explicit and sent once")
    portal.close()

    portal = Portal(browser, 1280)
    portal.join()
    portal.open()
    expect(portal.page.locator("#successPanel")).to_be_visible()
    portal.page.screenshot(path=str(OUTPUT / "success-1280.png"), full_page=True)
    check(portal.page.evaluate("document.documentElement.scrollWidth <= innerWidth"), "desktop success fits viewport")
    portal.page.locator("#changeWifi").click()
    expect(portal.page.locator("#setupPanel")).to_be_visible()
    check(portal.page.locator("#successPanel").is_hidden(), "change Wi-Fi returns to setup")
    portal.close()

    portal = Portal(browser)
    portal.open()
    portal.page.locator("#network").select_option("Lab Guest")
    check(portal.page.locator("#passwordGroup").is_hidden(), "open network needs no password field")
    portal.page.locator("#connect").click()
    expect(portal.page.locator("#noticeTitle")).to_have_text("配置已保存")
    check(portal.posts[0]["password"] == [""], "open network submits an empty password")
    portal.status.update(state="retrying", message="测试：路由器拒绝认证。", reason=202)
    expect(portal.page.locator("#noticeTitle")).to_have_text("尚未连接成功", timeout=6500)
    check("拒绝认证" in portal.page.locator("#noticeText").text_content(), "connection failure explains reason")
    check(portal.page.locator("#successPanel").is_hidden(), "failed connection never shows success")
    expect(portal.page.locator("#connect")).to_be_enabled()
    portal.close()

    portal = Portal(browser)
    portal.networks = []
    portal.open()
    check(portal.page.locator("#noNetworks").is_visible(), "empty scan offers recovery")
    portal.page.locator("#manual summary").click()
    portal.page.locator("#manualSsid").fill("Hidden Lab")
    portal.page.locator("#manualOpen").check()
    portal.save_error = True
    portal.page.locator("#connect").click()
    expect(portal.page.locator("#noticeTitle")).to_have_text("尚未确认保存成功")
    check(portal.posts[0]["ssid"] == ["Hidden Lab"], "hidden network uses manual SSID")
    check("未能保存" in portal.page.locator("#noticeText").text_content(), "save error is shown without claiming success")
    portal.close()

    portal = Portal(browser)
    portal.join()
    portal.status["station_ip"] = "999.1.1.1"
    portal.open()
    check(portal.page.locator("#successPanel").is_hidden(), "invalid IP cannot show a connected success screen")
    portal.close()


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--browser", choices=["chromium", "msedge"], default="chromium")
    args = parser.parse_args()
    with sync_playwright() as playwright:
        launch_args = dict(headless=True)
        if args.browser == "msedge":
            launch_args["channel"] = "msedge"
        browser = playwright.chromium.launch(**launch_args)
        try:
            run(browser)
        finally:
            browser.close()
    print(f"Portal browser checks passed: {checks}\nScreenshots: {OUTPUT}")
