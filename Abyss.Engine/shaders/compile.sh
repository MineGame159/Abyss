compile() {
    slangc -target spirv -O2 -fvk-use-entrypoint-name -o bin/$1.spv src/$1.slang
}

compile mesh
