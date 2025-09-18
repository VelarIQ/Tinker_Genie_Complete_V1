#!/bin/bash
cd /var/www/tinker-genie/TinkerGenie.API
export ASPNETCORE_ENVIRONMENT=Production
export ASPNETCORE_URLS=http://0.0.0.0:8080
exec dotnet run --no-build
