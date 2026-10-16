FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/GmodAddonAnalyzer/GmodAddonAnalyzer.csproj ./GmodAddonAnalyzer/
RUN dotnet restore ./GmodAddonAnalyzer/GmodAddonAnalyzer.csproj
COPY src/GmodAddonAnalyzer/ ./GmodAddonAnalyzer/
RUN dotnet publish ./GmodAddonAnalyzer/GmodAddonAnalyzer.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
COPY --from=build /app/publish ./

# Run as an unprivileged user. The app writes only to /app/data (volume) and /tmp;
# a named /app/data volume inherits this ownership on first use.
RUN groupadd --gid 1001 app \
    && useradd --uid 1001 --gid app --home /home/app app \
    && mkdir -p /app/data \
    && chown -R app:app /app /home/app
USER app

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "GmodAddonAnalyzer.dll"]