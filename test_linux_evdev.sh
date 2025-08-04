#!/bin/bash

echo "Linux Keyboard Hook Test (evdev-based)"
echo "======================================"
echo "This test uses the new evdev-based keyboard hook implementation."
echo ""

# Check if we're on Linux
if [[ "$OSTYPE" != "linux-gnu"* ]]; then
    echo "This script is intended for Linux systems only."
    exit 1
fi

# Check for .NET
if ! command -v dotnet &> /dev/null; then
    echo ".NET not found. Please install .NET 8.0 or later."
    exit 1
fi

# Check for input device access
echo "Checking input device access..."
if [ ! -d "/dev/input" ]; then
    echo "Error: /dev/input directory not found"
    exit 1
fi

echo "Available input devices:"
ls -la /dev/input/event* 2>/dev/null || echo "No event devices found"

echo ""
echo "Note: This implementation requires:"
echo "1. Root privileges for global capture (run with sudo)"
echo "2. Access to input devices"
echo "3. Proper keycode mapping for your keyboard"
echo ""

# Build the project
echo "Building project..."
dotnet build Q9CS_CrossPlatform.csproj --configuration Release

if [ $? -eq 0 ]; then
    echo "Build successful!"
    echo ""
    echo "To run the application:"
    echo "  sudo dotnet run --project Q9CS_CrossPlatform.csproj --configuration Release"
    echo ""
    echo "Note: sudo is required for global keyboard capture on Linux."
else
    echo "Build failed!"
    exit 1
fi 