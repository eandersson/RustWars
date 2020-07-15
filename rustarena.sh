#!/usr/bin/env bash

# Enable debugging
#set -x

mkdir -p /steamcmd/rust/oxide/plugins
curl https://umod.org/plugins/QuickSmelt.cs --output /steamcmd/rust/oxide/plugins/QuickSmelt.cs

bash /app/start.sh