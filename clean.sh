#!/usr/bin/env bash

find . -type d \( -name "bin" -o -name "obj" -o -name "TestResults" \) -prune -exec rm -rf {} +

echo "Done."