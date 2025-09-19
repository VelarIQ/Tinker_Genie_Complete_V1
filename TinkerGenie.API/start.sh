#!/bin/bash
cd /var/www/tinker-genie/TinkerGenie.API
export ASPNETCORE_URLS="http://0.0.0.0:8080"
dotnet run
