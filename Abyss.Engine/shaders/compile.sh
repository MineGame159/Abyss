compile() {
    slangc -target spirv -O2 -fvk-use-entrypoint-name -o bin/$1.spv src/$1.slang
}

compile fullscreen

compile mesh
compile bloom_downsample
compile bloom_upsample
compile composite

compile imgui
