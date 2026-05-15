FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /source
COPY . .
WORKDIR /source/src/PortableContextMcpServer
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
RUN mkdir -p /data && chown -R app:app /data /app
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "PortableContextMcpServer.dll"]
