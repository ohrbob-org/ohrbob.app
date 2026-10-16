#!/bin/bash

# Get the directory where the script is located

#DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
DIR="/home/kaiwegner/Projects/android-emu"
# Set ANDROID_HOME to the local SDK directory
export ANDROID_HOME="$DIR/android-sdk"

# Add tools to PATH for convenience
export PATH="$ANDROID_HOME/emulator:$ANDROID_HOME/platform-tools:$PATH"

echo "Launching Android 11 emulator (android11)..."
echo "Note: If running over SSH without a display, append '-no-window' to the command."

# Launch the emulator, passing along any additional arguments (like -no-window)
"$ANDROID_HOME/emulator/emulator" -avd Pixel_API_34 "$@"


ohrbob://connect?server=http%3A%2F%2F10.0.2.2%3A5002&token=00ef0bbf-8177-4f5d-b308-b2843ab09d0c
