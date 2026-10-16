"""
Parity test runner: compares Python vs C# filter output.
Usage: python parity_filters.py
"""
import numpy as np
import subprocess, json, pathlib

def main():
    print("Parity tool — filter comparison (Python vs C#)")
    # Generate test signal
    rng = np.random.default_rng(42)
    fs = 250
    t = np.arange(fs * 10) / fs
    sig = rng.normal(0, 10, (len(t), 11)).astype(np.float32)
    np.save("test_signal.npy", sig)
    print("test_signal.npy saved. Run C# parity tool to compare.")

if __name__ == "__main__":
    main()
