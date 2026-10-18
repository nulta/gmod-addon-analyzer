FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/GmodAddonAnalyzer/GmodAddonAnalyzer.csproj ./GmodAddonAnalyzer/
RUN dotnet restore ./GmodAddonAnalyzer/GmodAddonAnalyzer.csproj
COPY src/GmodAddonAnalyzer/ ./GmodAddonAnalyzer/
RUN dotnet publish ./GmodAddonAnalyzer/GmodAddonAnalyzer.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
COPY --from=build /app/publish ./

# Steam's Linux SteamCMD binary is 32-bit and needs the 32-bit libgcc runtime,
# which is not in the .NET base image. Without it steamcmd.sh fails with
# "error while loading shared libraries: libgcc_s.so.1".
RUN apt-get update \
    && apt-get install -y --no-install-recommends lib32gcc-s1 \
    && rm -rf /var/lib/apt/lists/*

# Run as an unprivileged user. .NET 8+ base images already ship a non-root
# user/group `app` (APP_UID, home /app); only create uid 1001 on older bases.
# The app writes only to /app/data (volume, whose named volume inherits this
# ownership on first use) and /tmp.
RUN getent passwd app >/dev/null 2>&1 \
        || groupadd --gid 1001 app \
        && getent passwd app >/dev/null 2>&1 \
        || useradd --uid 1001 --gid app --create-home --home-dir /home/app app \
    && mkdir -p /app/data \
    && chown -R app:app /app
USER app

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "GmodAddonAnalyzer.dll"]