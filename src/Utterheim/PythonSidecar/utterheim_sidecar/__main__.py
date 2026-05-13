# Allow `python -m utterheim_sidecar ...` to dispatch to the typer app
# defined in main.py. Same shape pocket_tts uses (`python -m pocket_tts`).
from utterheim_sidecar.main import app

if __name__ == "__main__":
    app()
