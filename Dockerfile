FROM mcr.microsoft.com/dotnet/core/sdk:3.0 as builder

WORKDIR /app

RUN dotnet tool install -g fake-cli && \
    dotnet tool install -g paket
ENV PATH="$PATH:/root/.dotnet/tools"

COPY paket.dependencies .
COPY paket.lock .
COPY build.fsx .

RUN fake build

COPY . .
RUN fake build -t Publish

FROM mcr.microsoft.com/dotnet/core/runtime:3.0-alpine as runner

WORKDIR /app

COPY --from=builder /app/src/Printer/out .

ENTRYPOINT [ "dotnet", "Printer.dll" ]
