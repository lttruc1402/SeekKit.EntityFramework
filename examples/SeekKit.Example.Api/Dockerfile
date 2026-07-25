# Build context must be the repository root (see docker-compose.yml):
#   docker build -f examples/SeekKit.Example.Api/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props ./
COPY SeekKit.Core/ SeekKit.Core/
COPY SeekKit.EntityFramework/ SeekKit.EntityFramework/
COPY examples/SeekKit.Example.Api/ examples/SeekKit.Example.Api/

RUN dotnet publish examples/SeekKit.Example.Api/SeekKit.Example.Api.csproj \
    --configuration Release \
    --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "SeekKit.Example.Api.dll"]
