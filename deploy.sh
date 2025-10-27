#!/bin/bash

# Deployment script for Silksong TAS Info Tool
# Copies necessary files from bin to the game directory

SOURCE_DIR="bin/Silksong TAS Info Tool/v28714"
TARGET_DIR="$HOME/TAS/Games/Silksong/v28714"

echo "Deploying Silksong TAS Info Tool..."
echo "Source: $SOURCE_DIR"
echo "Target: $TARGET_DIR"

# Create target directories if they don't exist (if needed)
echo "Creating target directories..."
mkdir -p "$TARGET_DIR"
mkdir -p "$TARGET_DIR/Hollow Knight Silksong_Data/Managed"

# Copy lua and config files
echo "Copying TAS files..."
cp "$SOURCE_DIR/SilkTasInfo.lua" "$TARGET_DIR/" || exit 1

# Only copy config if it doesn't exist (to preserve user changes)
if [ ! -f "$TARGET_DIR/SilkTasInfo.config" ]; then
    echo "Copying initial config file..."
    cp "$SOURCE_DIR/SilkTasInfo.config" "$TARGET_DIR/" || exit 1
else
    echo "Config file already exists, skipping to preserve user changes..."
fi

# Copy modded assemblies to the Managed folder
echo "Copying modded assemblies..."
MANAGED_TARGET="$TARGET_DIR/Hollow Knight Silksong_Data/Managed"

# Main modded assembly
cp "$SOURCE_DIR/Hollow_Knight_Silksong_Data/Managed/Assembly-CSharp.dll" "$MANAGED_TARGET/" || exit 1

# MonoMod dependencies
cp "$SOURCE_DIR/Hollow_Knight_Silksong_Data/Managed/MonoMod.Utils.dll" "$MANAGED_TARGET/" || exit 1
cp "$SOURCE_DIR/Hollow_Knight_Silksong_Data/Managed/MonoMod.RuntimeDetour.dll" "$MANAGED_TARGET/" || exit 1

# Mono.Cecil dependencies
cp "$SOURCE_DIR/Hollow_Knight_Silksong_Data/Managed/Mono.Cecil.dll" "$MANAGED_TARGET/" || exit 1
cp "$SOURCE_DIR/Hollow_Knight_Silksong_Data/Managed/Mono.Cecil.Rocks.dll" "$MANAGED_TARGET/" || exit 1
cp "$SOURCE_DIR/Hollow_Knight_Silksong_Data/Managed/Mono.Cecil.Pdb.dll" "$MANAGED_TARGET/" || exit 1
cp "$SOURCE_DIR/Hollow_Knight_Silksong_Data/Managed/Mono.Cecil.Mdb.dll" "$MANAGED_TARGET/" || exit 1
cp "$SOURCE_DIR/Hollow_Knight_Silksong_Data/Managed/Mono.Security.dll" "$MANAGED_TARGET/" || exit 1

echo "Deployment complete!"