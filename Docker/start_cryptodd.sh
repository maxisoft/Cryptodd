#!/bin/sh
set -e
PUID=${PUID:-913}
usermod -u "$PUID" "${CRYPTO_DUMPER_USER_NAME:-cryptodd}" >/dev/null
PGID=${PGID:-913}
groupmod -g "$PGID" "${CRYPTO_DUMPER_GROUP_NAME:-cryptodd}" >/dev/null
APP_PATH=${APP_PATH:-/app}

CRYPTOdd_BASEPATH=${CRYPTOdd_BASEPATH:-/cryptodd}
# create folders
if [ ! -d "${CRYPTOdd_BASEPATH}" ]; then \
    mkdir -p "${CRYPTOdd_BASEPATH}"
    chown -R "$PUID:$PGID" "${CRYPTOdd_BASEPATH}" || :
fi

# check permissions
if [ ! "$(stat -c %u "${CRYPTOdd_BASEPATH}")" = "$PUID" ]; then
	echo "Change in ownership detected, please be patient while we chown existing files ..."
	chown "$PUID:$PGID" -R "${CRYPTOdd_BASEPATH}" || :
fi

cp --no-clobber "$APP_PATH/config.yaml" "${CRYPTOdd_BASEPATH}/config.yaml"
chown "$PUID:$PGID" "${CRYPTOdd_BASEPATH}/config.yaml" || :
cp --no-clobber "$APP_PATH/appsettings.json" "${CRYPTOdd_BASEPATH}/appsettings.json"
mkdir -p "${CRYPTOdd_BASEPATH}/Plugins"
if [ ! "$(stat -c %u "${CRYPTOdd_BASEPATH}/Plugins")" = "$PUID" ]; then
	chown -R "$PUID:$PGID" -R "${CRYPTOdd_BASEPATH}/Plugins" || :
fi
rm -rf /tmp/* || :

renice "+${NICE_ADJUSTEMENT:-3}" $$ >/dev/null 2>&1 || :
exec ionice -c "${IONICE_CLASS:-2}" -n "${IONICE_CLASSDATA:-5}" -t su-exec "$PUID:$PGID" "dotnet" "${APP_PATH}/Cryptodd.Console.dll" $@