FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/GmodAddonAnalyzer/GmodAddonAnalyzer.csproj ./GmodAddonAnalyzer/
RUN dotnet restore ./GmodAddonAnalyzer/GmodAddonAnalyzer.csproj
COPY src/GmodAddonAnalyzer/ ./GmodAddonAnalyzer/
RUN dotnet publish ./GmodAddonAnalyzer/GmodAddonAnalyzer.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
COPY --from=build /app/publish ./
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "GmodAddonAnalyzer.dll"]