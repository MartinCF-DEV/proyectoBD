# Etapa 1: Compilar Frontend Angular
FROM node:22-alpine AS frontend-build
WORKDIR /src/frontend
COPY frontend/package*.json ./
RUN npm install
COPY frontend/ ./
RUN npm run build

# Etapa 2: Compilar Backend .NET 10 (C#)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend-build
WORKDIR /src/backend
COPY backend/Community360.Api.csproj ./
RUN dotnet restore
COPY backend/ ./
RUN dotnet publish -c Release -o /app/publish

# Etapa 3: Imagen final de ejecución (.NET 10 + datos depurados + frontend)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=backend-build /app/publish ./
COPY --from=frontend-build /src/frontend/dist/community360-frontend/browser ./wwwroot
COPY salida/ ./salida/
COPY datos/ ./datos/
COPY backend/secure-connect-proyecto.zip ./secure-connect-proyecto.zip
COPY etl_comunidad_virtual.py ./

ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

ENTRYPOINT ["dotnet", "Community360.Api.dll"]
