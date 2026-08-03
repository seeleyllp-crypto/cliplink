from __future__ import annotations

import os
import http.client
import queue
import re
import secrets
import shutil
import subprocess
import sys
import tempfile
import threading
import webbrowser
from pathlib import Path
from tkinter import BooleanVar, StringVar, Tk, messagebox
from tkinter import ttk


APP_NAME = "ClipLink"
APP_VERSION = "1.1"
LITTERBOX_HOST = "litterbox.catbox.moe"
LITTERBOX_API_PATH = "/resources/internals/api.php"
LITTERBOX_RETENTION = "72h"
MAX_UPLOAD_BYTES = 1_000_000_000
YOUTUBE_RE = re.compile(r"^https?://(?:www\.|m\.)?(?:youtube\.com|youtu\.be)/", re.I)


def app_dir() -> Path:
    if getattr(sys, "frozen", False):
        return Path(sys.executable).resolve().parent
    return Path(__file__).resolve().parent


def find_tool(name: str) -> str | None:
    local = app_dir() / f"{name}.exe"
    if local.exists():
        return str(local)
    return shutil.which(name)


def choose_temp_root() -> Path:
    """Choose a writable temp location with enough room for yt-dlp and a video."""
    candidates: list[Path] = []
    system_temp = Path(tempfile.gettempdir())
    candidates.append(system_temp)
    if os.name == "nt":
        for letter in "DEFGHIJKLMNOPQRSTUVWXYZC":
            drive = Path(f"{letter}:\\")
            if drive.exists():
                candidates.append(drive / "ClipLink-Temp")
    seen: set[str] = set()
    for candidate in candidates:
        key = str(candidate).lower()
        if key in seen:
            continue
        seen.add(key)
        try:
            candidate.mkdir(parents=True, exist_ok=True)
            if shutil.disk_usage(candidate).free >= 1200 * 1024 * 1024:
                return candidate
        except OSError:
            continue
    raise RuntimeError("ClipLink needs at least 1.2 GB of free disk space on one drive.")


class ClipLinkApp:
    def __init__(self, root: Tk) -> None:
        self.root = root
        self.root.title(f"{APP_NAME} — YouTube to MP4 URL")
        self.root.geometry("720x520")
        self.root.minsize(640, 480)
        self.root.configure(bg="#0b1220")

        self.url = StringVar()
        self.rights_confirmed = BooleanVar(value=False)
        self.result_url = StringVar()
        self.status = StringVar(value="Ready")
        self.events: queue.Queue[tuple[str, object]] = queue.Queue()
        self.worker: threading.Thread | None = None
        self.current_process: subprocess.Popen[str] | None = None
        self.cancel_requested = threading.Event()

        self._configure_styles()
        self._build_ui()
        self.root.after(100, self._drain_events)

    def _configure_styles(self) -> None:
        style = ttk.Style()
        style.theme_use("clam")
        style.configure("App.TFrame", background="#0b1220")
        style.configure("Card.TFrame", background="#111c2f")
        style.configure("Title.TLabel", background="#0b1220", foreground="#f8fafc", font=("Segoe UI Semibold", 24))
        style.configure("Sub.TLabel", background="#0b1220", foreground="#9fb0c8", font=("Segoe UI", 10))
        style.configure("Card.TLabel", background="#111c2f", foreground="#dce7f7", font=("Segoe UI", 10))
        style.configure("Hint.TLabel", background="#111c2f", foreground="#8fa2bd", font=("Segoe UI", 9))
        style.configure("Card.TCheckbutton", background="#111c2f", foreground="#dce7f7", font=("Segoe UI", 10))
        style.map("Card.TCheckbutton", background=[("active", "#111c2f")])
        style.configure("Primary.TButton", font=("Segoe UI Semibold", 10), padding=(14, 10), background="#22c55e", foreground="#04110a")
        style.map("Primary.TButton", background=[("active", "#4ade80"), ("disabled", "#344256")])
        style.configure("Secondary.TButton", font=("Segoe UI", 10), padding=(12, 9), background="#263750", foreground="#edf4ff")
        style.map("Secondary.TButton", background=[("active", "#334a6b")])
        style.configure("Horizontal.TProgressbar", troughcolor="#263750", background="#22c55e", bordercolor="#263750")

    def _build_ui(self) -> None:
        main = ttk.Frame(self.root, style="App.TFrame", padding=24)
        main.pack(fill="both", expand=True)

        ttk.Label(main, text="ClipLink", style="Title.TLabel").pack(anchor="w")
        ttk.Label(main, text="Paste a permitted YouTube video. ClipLink handles the rest.", style="Sub.TLabel").pack(anchor="w", pady=(2, 18))

        card = ttk.Frame(main, style="Card.TFrame", padding=18)
        card.pack(fill="x")
        card.columnconfigure(0, weight=1)

        ttk.Label(card, text="YouTube URL", style="Card.TLabel").grid(row=0, column=0, columnspan=2, sticky="w")
        self.url_entry = ttk.Entry(card, textvariable=self.url, font=("Segoe UI", 11))
        self.url_entry.grid(row=1, column=0, columnspan=2, sticky="ew", pady=(6, 12), ipady=6)
        ttk.Checkbutton(card, text="I own this video or have permission to download and share it", variable=self.rights_confirmed, style="Card.TCheckbutton").grid(row=2, column=0, columnspan=2, sticky="w", pady=2)
        ttk.Label(card, text="One click downloads the MP4, uploads it to Litterbox for 72 hours, copies the URL, and deletes the temporary MP4. Limit: 1 GB.", style="Hint.TLabel", wraplength=620).grid(row=3, column=0, columnspan=2, sticky="w", pady=(6, 0))

        actions = ttk.Frame(main, style="App.TFrame")
        actions.pack(fill="x", pady=(14, 10))
        self.start_button = ttk.Button(actions, text="Make Public MP4 URL", style="Primary.TButton", command=self._start)
        self.start_button.pack(side="left")
        self.cancel_button = ttk.Button(actions, text="Cancel", style="Secondary.TButton", command=self._cancel, state="disabled")
        self.cancel_button.pack(side="left", padx=(10, 0))
        ttk.Label(actions, textvariable=self.status, style="Sub.TLabel").pack(side="right")

        self.progress = ttk.Progressbar(main, mode="indeterminate")
        self.progress.pack(fill="x", pady=(0, 12))

        result = ttk.Frame(main, style="Card.TFrame", padding=14)
        result.pack(fill="x")
        result.columnconfigure(0, weight=1)
        ttk.Label(result, text="Result URL", style="Card.TLabel").grid(row=0, column=0, columnspan=3, sticky="w")
        self.result_entry = ttk.Entry(result, textvariable=self.result_url, state="readonly")
        self.result_entry.grid(row=1, column=0, sticky="ew", pady=(6, 0), ipady=5)
        ttk.Button(result, text="Copy", style="Secondary.TButton", command=self._copy).grid(row=1, column=1, padx=(8, 0), pady=(6, 0))
        ttk.Button(result, text="Open", style="Secondary.TButton", command=self._open_result).grid(row=1, column=2, padx=(8, 0), pady=(6, 0))

        log_card = ttk.Frame(main, style="Card.TFrame", padding=12)
        log_card.pack(fill="both", expand=True, pady=(12, 0))
        from tkinter import Text
        self.log = Text(log_card, height=7, bg="#08111f", fg="#b9c9dc", insertbackground="#ffffff", relief="flat", font=("Consolas", 9), wrap="word", padx=10, pady=8)
        self.log.pack(fill="both", expand=True)
        self.log.configure(state="disabled")
        self._log("Paste a YouTube link to begin.")
        self.url_entry.focus_set()

    def _log(self, message: str) -> None:
        self.log.configure(state="normal")
        self.log.insert("end", message.rstrip() + "\n")
        self.log.see("end")
        self.log.configure(state="disabled")

    def _start(self) -> None:
        url = self.url.get().strip()
        if not YOUTUBE_RE.match(url):
            messagebox.showerror(APP_NAME, "Enter a valid youtube.com or youtu.be URL.")
            return
        if not self.rights_confirmed.get():
            messagebox.showwarning(APP_NAME, "Confirm that you own the video or have permission to download and share it.")
            return
        if not find_tool("yt-dlp"):
            messagebox.showerror(APP_NAME, "yt-dlp.exe was not found. Put it beside ClipLink.exe or install yt-dlp.")
            return

        self.result_url.set("")
        self.cancel_requested.clear()
        self.start_button.configure(state="disabled")
        self.cancel_button.configure(state="normal")
        self.progress.start(12)
        self.status.set("Starting…")
        self.worker = threading.Thread(target=self._run_job, args=(url,), daemon=True)
        self.worker.start()

    def _cancel(self) -> None:
        self.cancel_requested.set()
        proc = self.current_process
        if proc and proc.poll() is None:
            proc.terminate()
        self.status.set("Cancelling…")

    def _run_process(self, args: list[str], temp_root: Path | None = None) -> tuple[int, list[str]]:
        creationflags = subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0
        process_env = os.environ.copy()
        if temp_root is not None:
            process_env["TEMP"] = str(temp_root)
            process_env["TMP"] = str(temp_root)
        proc = subprocess.Popen(args, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, encoding="utf-8", errors="replace", bufsize=1, creationflags=creationflags, env=process_env)
        self.current_process = proc
        lines: list[str] = []
        assert proc.stdout is not None
        for raw in proc.stdout:
            line = raw.strip()
            if line:
                lines.append(line)
                self.events.put(("log", line))
            if self.cancel_requested.is_set():
                proc.terminate()
                break
        code = proc.wait()
        self.current_process = None
        return code, lines

    def _upload_litterbox(self, file_path: Path) -> str:
        boundary = "----ClipLink" + secrets.token_hex(16)
        before = (
            f"--{boundary}\r\n"
            'Content-Disposition: form-data; name="reqtype"\r\n\r\n'
            "fileupload\r\n"
            f"--{boundary}\r\n"
            'Content-Disposition: form-data; name="time"\r\n\r\n'
            f"{LITTERBOX_RETENTION}\r\n"
            f"--{boundary}\r\n"
            'Content-Disposition: form-data; name="fileToUpload"; filename="video.mp4"\r\n'
            "Content-Type: video/mp4\r\n\r\n"
        ).encode("utf-8")
        after = f"\r\n--{boundary}--\r\n".encode("utf-8")
        total = len(before) + file_path.stat().st_size + len(after)
        connection = http.client.HTTPSConnection(LITTERBOX_HOST, timeout=120)
        try:
            connection.putrequest("POST", LITTERBOX_API_PATH)
            connection.putheader("Content-Type", f"multipart/form-data; boundary={boundary}")
            connection.putheader("Content-Length", str(total))
            connection.putheader("User-Agent", f"{APP_NAME}/{APP_VERSION}")
            connection.endheaders()
            connection.send(before)
            sent = 0
            next_report = 10
            with file_path.open("rb") as stream:
                while chunk := stream.read(1024 * 1024):
                    if self.cancel_requested.is_set():
                        raise RuntimeError("Cancelled")
                    connection.send(chunk)
                    sent += len(chunk)
                    percent = int(sent * 100 / file_path.stat().st_size)
                    if percent >= next_report:
                        self.events.put(("log", f"Upload: {min(percent, 100)}%"))
                        next_report += 10
            connection.send(after)
            response = connection.getresponse()
            body = response.read().decode("utf-8", errors="replace").strip()
            if response.status < 200 or response.status >= 300 or not body.startswith("https://"):
                raise RuntimeError(f"Litterbox upload failed ({response.status}): {body or response.reason}")
            return body
        finally:
            connection.close()

    def _run_job(self, url: str) -> None:
        downloaded: Path | None = None
        temp_root = choose_temp_root()
        folder = Path(tempfile.mkdtemp(prefix="ClipLink-", dir=temp_root))
        try:
            self.events.put(("log", f"Temporary drive: {temp_root.drive or temp_root}"))
            self.events.put(("status", "Downloading MP4…"))
            ytdlp = find_tool("yt-dlp")
            assert ytdlp
            command = [
                ytdlp, "--no-playlist", "--windows-filenames", "--newline",
                "--extractor-args", "youtube:player_client=android_vr,android,ios",
                "-f", "b[ext=mp4]/b", "--max-filesize", "1000M", "--print", "after_move:filepath",
                "-o", str(folder / "%(title).120s [%(id)s].%(ext)s"), url,
            ]
            code, lines = self._run_process(command, temp_root=temp_root)
            if self.cancel_requested.is_set():
                raise RuntimeError("Cancelled")
            if code != 0:
                raise RuntimeError("yt-dlp could not download this video. See the activity log for details.")
            for line in reversed(lines):
                candidate = Path(line)
                if candidate.suffix.lower() == ".mp4" and candidate.exists():
                    downloaded = candidate
                    break
            if not downloaded:
                candidates = sorted(folder.glob("*.mp4"), key=lambda p: p.stat().st_mtime, reverse=True)
                downloaded = candidates[0] if candidates else None
            if not downloaded or not downloaded.exists():
                raise RuntimeError("The download finished, but the MP4 could not be located.")
            size = downloaded.stat().st_size
            self.events.put(("log", f"Downloaded temporary MP4 ({size / 1024 / 1024:.1f} MB)."))
            if size > MAX_UPLOAD_BYTES:
                raise RuntimeError("The MP4 is larger than Litterbox's 1 GB upload limit.")
            self.events.put(("status", "Uploading to Litterbox for 72 hours…"))
            public_url = self._upload_litterbox(downloaded)
            self.events.put(("result", public_url))
            self.events.put(("copy", public_url))
            self.events.put(("log", "Temporary 72-hour URL created and copied to the clipboard."))
            self.events.put(("done", "Complete"))
        except Exception as exc:
            self.events.put(("error", str(exc)))
        finally:
            shutil.rmtree(folder, ignore_errors=True)

    def _drain_events(self) -> None:
        try:
            while True:
                kind, payload = self.events.get_nowait()
                if kind == "log":
                    self._log(str(payload))
                elif kind == "status":
                    self.status.set(str(payload))
                elif kind == "result":
                    self.result_url.set(str(payload))
                elif kind == "copy":
                    self.root.clipboard_clear()
                    self.root.clipboard_append(str(payload))
                elif kind == "done":
                    self._finish(str(payload))
                    messagebox.showinfo(APP_NAME, "Finished successfully.")
                elif kind == "error":
                    self._finish("Cancelled" if str(payload) == "Cancelled" else "Failed")
                    if str(payload) != "Cancelled":
                        messagebox.showerror(APP_NAME, str(payload))
        except queue.Empty:
            pass
        self.root.after(100, self._drain_events)

    def _finish(self, state: str) -> None:
        self.progress.stop()
        self.status.set(state)
        self.start_button.configure(state="normal")
        self.cancel_button.configure(state="disabled")

    def _copy(self) -> None:
        value = self.result_url.get().strip()
        if value:
            self.root.clipboard_clear()
            self.root.clipboard_append(value)
            self.status.set("URL copied")

    def _open_result(self) -> None:
        value = self.result_url.get().strip()
        if value:
            webbrowser.open(value)


def main() -> None:
    if "--self-test" in sys.argv:
        ytdlp = find_tool("yt-dlp")
        if not ytdlp:
            print("SELF_TEST_FAILED yt-dlp=False")
            raise SystemExit(1)
        print(f"SELF_TEST_OK yt-dlp={ytdlp}")
        return
    root = Tk()
    ClipLinkApp(root)
    root.mainloop()


if __name__ == "__main__":
    main()
