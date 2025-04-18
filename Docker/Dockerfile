# Dockerfile for self-contained RewindSubtitleDisplayerForPlex (Config in /app)
# Command to build image (don't forget the period):
#		docker build -t thios-plex-rewind-subtitle-displayer .
# Then the docker compose file (See the command in that file itself)

# Use the official .NET runtime dependencies image for Debian 12 (Bookworm) Slim.
# This uses glibc and should be more compatible with standard linux-x64 builds.
ARG DOTNET_VERSION=9.0 # Adjust if needed
FROM mcr.microsoft.com/dotnet/runtime-deps:${DOTNET_VERSION}-bookworm-slim

# Set the working directory inside the container
WORKDIR /app

# Copy the pre-compiled self-contained binary into the image's /app directory
COPY RewindSubtitleDisplayerForPlex_*_linux-x64 /app/RewindSubtitleDisplayerForPlex

# Make the binary executable
RUN chmod +x /app/RewindSubtitleDisplayerForPlex

# VOLUME instruction removed - we will mount directly to /app via docker run

# Set the entrypoint to run the application executable
ENTRYPOINT ["/app/RewindSubtitleDisplayerForPlex"]