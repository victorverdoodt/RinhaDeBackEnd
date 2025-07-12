FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER app
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
    clang zlib1g-dev
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["RinhaDeBackEnd-AOT/RinhaDeBackEnd-AOT.csproj", "RinhaDeBackEnd-AOT/"]
RUN dotnet restore "./RinhaDeBackEnd-AOT/./RinhaDeBackEnd-AOT.csproj"
COPY . .
WORKDIR "/src/RinhaDeBackEnd-AOT"
RUN dotnet build "./RinhaDeBackEnd-AOT.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./RinhaDeBackEnd-AOT.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=true --self-contained

FROM mcr.microsoft.com/dotnet/runtime-deps:8.0 AS final

USER root
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
EXPOSE 8080
COPY --from=publish /app/publish .

USER app

ENTRYPOINT ["./RinhaDeBackEnd-AOT"]