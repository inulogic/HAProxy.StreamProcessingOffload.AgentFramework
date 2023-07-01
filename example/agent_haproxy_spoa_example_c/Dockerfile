FROM alpine:3.18 AS base
WORKDIR /app
EXPOSE 12345

FROM base AS build
RUN set -ex && \
    apk add --no-cache gcc musl-dev libevent-dev make git
WORKDIR /src
RUN git clone https://github.com/haproxy/spoa-example.git \
 && cd spoa-example \
 && make

FROM build AS publish
RUN cd spoa-example && make install BINDIR=/app/spoa

FROM base AS final
RUN set -ex && \
    apk add --no-cache libevent
WORKDIR /app
COPY --from=publish /app/spoa .
ENTRYPOINT ["./spoa"]