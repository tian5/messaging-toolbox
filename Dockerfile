# syntax=docker/dockerfile:1

# ---- Build stage -------------------------------------------------------------
# Restore runs from project files only, so the restore layer caches independently
# of source changes (DevOps guidelines §3).
# The SDK runs on the build machine's platform and cross-compiles for the target, so multi-arch
# builds never run the SDK under QEMU emulation (slow, and known to crash restore).
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
ARG TARGETARCH
WORKDIR /source

COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/MessagingToolbox.Abstractions/MessagingToolbox.Abstractions.csproj src/MessagingToolbox.Abstractions/
COPY src/MessagingToolbox.Domain/MessagingToolbox.Domain.csproj src/MessagingToolbox.Domain/
COPY src/MessagingToolbox.Application/MessagingToolbox.Application.csproj src/MessagingToolbox.Application/
COPY src/MessagingToolbox.Infrastructure/MessagingToolbox.Infrastructure.csproj src/MessagingToolbox.Infrastructure/
COPY src/MessagingToolbox.Providers.AzureServiceBus/MessagingToolbox.Providers.AzureServiceBus.csproj src/MessagingToolbox.Providers.AzureServiceBus/
COPY src/MessagingToolbox.Providers.RabbitMQ/MessagingToolbox.Providers.RabbitMQ.csproj src/MessagingToolbox.Providers.RabbitMQ/
COPY src/MessagingToolbox.ServiceDefaults/MessagingToolbox.ServiceDefaults.csproj src/MessagingToolbox.ServiceDefaults/
COPY src/MessagingToolbox.Web/MessagingToolbox.Web.csproj src/MessagingToolbox.Web/
RUN dotnet restore src/MessagingToolbox.Web/MessagingToolbox.Web.csproj -a $TARGETARCH

COPY src/ src/
# No --no-restore on publish: .NET 10 SDK drops blazor.web.js from publish output otherwise.
RUN dotnet publish src/MessagingToolbox.Web/MessagingToolbox.Web.csproj \
    -a $TARGETARCH --configuration Release --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app

# Single HTTP port; configuration is purely environment variables so the same
# image serves Aspire injection and standalone `docker run` scenarios.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# Writable tool-state directory (SQLite + Data Protection keys) for the non-root user.
# Pre-creating it also seeds the correct ownership into empty named volumes.
RUN mkdir -p /app/data && chown -R $APP_UID /app/data

USER $APP_UID

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "MessagingToolbox.Web.dll"]
