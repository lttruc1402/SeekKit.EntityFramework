# Build context must be the repository root (see docker-compose.yml):
#   docker build -f examples/SeekKit.Example.MongoApi/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props ./
COPY SeekKit.Core/ SeekKit.Core/
COPY SeekKit.MongoDB/ SeekKit.MongoDB/
COPY examples/SeekKit.Example.MongoApi/ examples/SeekKit.Example.MongoApi/

RUN dotnet publish examples/SeekKit.Example.MongoApi/SeekKit.Example.MongoApi.csproj \
    --configuration Release \
    --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "SeekKit.Example.MongoApi.dll"]
