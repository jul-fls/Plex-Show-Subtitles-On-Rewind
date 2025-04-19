# Dockerfile for self-contained RewindSubtitleDisplayerForPlex (Config in /app)
# Command to build image (don't forget the period):
#		docker build -t thios-plex-rewind-subtitle-displayer .
# Then the docker compose file (See the command in that file itself)

# Use the official .NET runtime dependencies image for Debian 12 (Bookworm) Slim.
# This uses glibc and should be more compatible with standard linux-x64 builds.
ARG DOTNET_VERSION=9.0 # Adjust if needed
# Stage 1: build with the full .NET SDK
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION}-bookworm-slim AS builder
WORKDIR /src

# Copy just the csproj and restore first to leverage caching
COPY *.sln .
COPY *.csproj ./RewindSubtitleDisplayerForPlex/
RUN dotnet restore

# Copy everything else & publish
COPY . .
RUN dotnet publish \
    --configuration Release \
    --runtime linux-x64 \
    --self-contained false \
    --output /app/publish

# Stage 2: runtime image
FROM mcr.microsoft.com/dotnet/runtime-deps:${DOTNET_VERSION}-bookworm-slim
WORKDIR /app

# Copy the published binary and supporting files
COPY --from=builder /app/publish .

# Make sure the entrypoint is executable
RUN chmod +x RewindSubtitleDisplayerForPlex*

ENTRYPOINT ["./RewindSubtitleDisplayerForPlex"]
