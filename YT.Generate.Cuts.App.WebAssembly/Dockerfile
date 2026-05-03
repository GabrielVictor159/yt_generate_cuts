FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY yt_generate_cuts.sln .
COPY YT.Generate.Cuts.App.WebAssembly/ YT.Generate.Cuts.App.WebAssembly/

RUN dotnet restore ./YT.Generate.Cuts.App.WebAssembly/YT.Generate.Cuts.App.WebAssembly.csproj

WORKDIR /src/YT.Generate.Cuts.App.WebAssembly

RUN dotnet publish "YT.Generate.Cuts.App.WebAssembly.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM nginx:alpine AS final
WORKDIR /usr/share/nginx/html


COPY YT.Generate.Cuts.App.WebAssembly/nginx.conf /etc/nginx/conf.d/default.conf

COPY --from=build /app/publish/wwwroot . 

EXPOSE 80
ENTRYPOINT ["nginx", "-g", "daemon off;"]