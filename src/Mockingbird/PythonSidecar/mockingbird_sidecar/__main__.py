# Allow `python -m mockingbird_sidecar ...` to dispatch to the typer app
# defined in main.py. Same shape pocket_tts uses (`python -m pocket_tts`).
from mockingbird_sidecar.main import app

if __name__ == "__main__":
    app()
