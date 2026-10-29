#!/bin/bash
set -e

# Path to the Desktop Head Unit inside your custom SDK
DHU_PATH="$HOME/Projects/android-emu/android-sdk/extras/google/auto/desktop-head-unit"
ADB_PATH="$HOME/Projects/android-emu/android-sdk/platform-tools/adb"

if [ ! -f "$DHU_PATH" ]; then
  echo "Error: DHU not found at $DHU_PATH."
  exit 1
fi

# Fallback to system adb if platform-tools adb isn't found
if [ ! -f "$ADB_PATH" ]; then
  ADB_PATH="adb"
fi

# Make sure the app is built and installed first
dotnet build Ohrbob.App.Android -t:Install -c Debug -p:AndroidSdkDirectory="$HOME/Projects/android-emu/android-sdk"

# Launch the Android Auto projection service on the phone/emulator
$ADB_PATH shell am start -n com.google.android.projection.gearhead/com.google.android.apps.auto.components.system.projection.ProjectionActivity

# Forward the port that DHU uses to talk to the emulator
$ADB_PATH forward tcp:5277 tcp:5277

echo "Starting Desktop Head Unit..."
# Run the Desktop Head Unit in the background
$DHU_PATH &
