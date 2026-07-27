#!/usr/bin/env bash
set -euo pipefail

sudo apt-get update
sudo apt-get install -y rpm
sudo gem install --no-document fpm
