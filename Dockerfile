# Build the browser app with a same-origin API URL.
FROM node:22-alpine AS frontend
WORKDIR /web
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
ENV VITE_API_URL=/api
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY backend/ ./
RUN dotnet restore LawLms.Api.csproj
RUN dotnet publish LawLms.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish ./
COPY --from=frontend /web/dist ./wwwroot
# Synthetic preview fixtures; restored from the image after a free-tier restart.
COPY deployment/preview-uploads/ /data/uploads/
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080
ENV Storage__RootPath=/data/uploads
EXPOSE 8080
ENTRYPOINT ["dotnet", "LawLms.Api.dll"]
