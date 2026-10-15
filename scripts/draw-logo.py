"""Package the exact user-approved transparent IND70 logo for 行商助手.

Source: src/Listener.App/Assets/listener-source.png. PNG resources are copied
unchanged; the Windows exporter only resamples the required ICO frames.
Usage: python scripts/draw-logo.py
"""
from pathlib import Path
import subprocess

if __name__ == "__main__":
    subprocess.run(
        ["powershell.exe", "-NoProfile", "-WindowStyle", "Hidden", "-ExecutionPolicy", "Bypass", "-STA", "-File",
         str(Path(__file__).with_name("export-icon.ps1"))],
        check=True,
        creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
    )
