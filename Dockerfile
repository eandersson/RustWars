FROM didstopia/rust-server:latest

ADD rustarena.sh /app/rustarena.sh

ENV RUST_SERVER_STARTUP_ARGUMENTS "-batchmode -load -nographics +server.secure 1 +server.levelurl http://eandersson.net/arena.map"
ENV RUST_SERVER_IDENTITY "rustwars"
ENV RUST_SERVER_NAME "RustWars"
ENV RUST_SERVER_DESCRIPTION "RustWars"
ENV RUST_OXIDE_ENABLED "1"
ENV RUST_OXIDE_UPDATE_ON_BOOT "1"
ENV RUST_SERVER_MAXPLAYERS "32"

RUN mkdir -p /steamcmd/rust/oxide/plugins /steamcmd/rust/oxide/config
ADD plugins/* /steamcmd/rust/oxide/plugins/
ADD config/* /steamcmd/rust/oxide/config/

CMD [ "bash", "/app/rustarena.sh" ]