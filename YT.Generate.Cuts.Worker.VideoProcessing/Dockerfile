FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER root

RUN apt-get update && \
    apt-get install -y ffmpeg && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/*

USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["YT.Generate.Cuts.Worker.VideoProcessing/YT.Generate.Cuts.Worker.VideoProcessing.csproj", "YT.Generate.Cuts.Worker.VideoProcessing/"]
COPY ["YT.Generate.Cuts.Application.Abstractions/YT.Generate.Cuts.Application.Abstractions.csproj", "YT.Generate.Cuts.Application.Abstractions/"]
COPY ["YT.Generate.Cuts.Application.VideoProcessing/YT.Generate.Cuts.Application.VideoProcessing.csproj", "YT.Generate.Cuts.Application.VideoProcessing/"]
COPY ["YT.Generate.Cuts.Infra.Data.Abstractions/YT.Generate.Cuts.Infra.Data.Abstractions.csproj", "YT.Generate.Cuts.Infra.Data.Abstractions/"]
COPY ["YT.Generate.Cuts.Infra.Data/YT.Generate.Cuts.Infra.Data.csproj", "YT.Generate.Cuts.Infra.Data/"]
COPY ["YT.Generate.Cuts.Domain/YT.Generate.Cuts.Domain.csproj", "YT.Generate.Cuts.Domain/"]

RUN dotnet restore "./YT.Generate.Cuts.Worker.VideoProcessing/YT.Generate.Cuts.Worker.VideoProcessing.csproj"
COPY . .
WORKDIR "/src/YT.Generate.Cuts.Worker.VideoProcessing"
RUN dotnet build "./YT.Generate.Cuts.Worker.VideoProcessing.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./YT.Generate.Cuts.Worker.VideoProcessing.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "YT.Generate.Cuts.Worker.VideoProcessing.dll"]