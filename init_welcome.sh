
#!/bin/sh
DATE=$(date +%Y%m%d)

[ -z "${SUDO_USER}" ] &&
    cat $HOME/.custom/motd
    echo -e '\n\n   👋 Welcome to Cloud Shell! 💻\n'

echo ''
