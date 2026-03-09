# 1. Basisimage mit .NET 9 SDK zum Bauen
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

# 2. Arbeitsverzeichnis im Container setzen
WORKDIR /app

# 3. Alle Projektdateien kopieren
COPY . .

# 4. Abhängigkeiten wiederherstellen
RUN dotnet restore

# 5. Projekt bauen
RUN dotnet publish -c Release -o out

# ---- Neues Stage für Laufzeit ----
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime

WORKDIR /app

# 6. Gebaute Dateien aus vorheriger Stage kopieren
COPY --from=build /app/out .

# 7. App starten
ENTRYPOINT ["dotnet", "LOCKnet.dll"]
