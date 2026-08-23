# Este projeto e um Blazor Web App (Microsoft.NET.Sdk.Web): o Kestrel embutido
# serve as paginas e os assets estaticos, entao nao ha nginx no estagio final.

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["YT.Generate.Cuts.App.WebAssembly/YT.Generate.Cuts.App.WebAssembly.csproj", "YT.Generate.Cuts.App.WebAssembly/"]
COPY ["YT.Generate.Cuts.Domain/YT.Generate.Cuts.Domain.csproj", "YT.Generate.Cuts.Domain/"]

RUN dotnet restore "./YT.Generate.Cuts.App.WebAssembly/YT.Generate.Cuts.App.WebAssembly.csproj"

COPY . .
WORKDIR "/src/YT.Generate.Cuts.App.WebAssembly"
RUN dotnet build "./YT.Generate.Cuts.App.WebAssembly.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./YT.Generate.Cuts.App.WebAssembly.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "YT.Generate.Cuts.App.WebAssembly.dll"]
