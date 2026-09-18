#!/usr/bin/env bash
# Build on the target Linux architecture, with glibc <= 2.31. No root writes.
set -Eeuo pipefail
export LC_ALL=C
umask 022

die() { printf 'ERROR: %s\n' "$*" >&2; exit 1; }
[[ $# == 1 ]] || die "Usage: bash $0 /absolute/path/to/NEW-build-directory"
[[ $(uname -s) == Linux ]] || die 'Run on Linux, not Windows/macOS.'
case "$(uname -m)" in
  aarch64|arm64) rid=linux-arm64; elf_machine=AArch64 ;;
  x86_64) rid=linux-x64; elf_machine='Advanced Micro Devices X86-64' ;;
  *) die 'Only native ARM64 or x64 builds are supported.' ;;
esac
for command in git cmake ninja python3 readelf getconf sha256sum; do
  command -v "$command" >/dev/null || die "Required tool missing: $command"
done
cc=${CC:-gcc}
cxx=${CXX:-g++}
command -v "$cc" >/dev/null || die "C compiler missing: $cc"
command -v "$cxx" >/dev/null || die "C++ compiler missing: $cxx"
glibc=$(getconf GNU_LIBC_VERSION) || die 'A glibc Linux build host is required.'
python3 - "$glibc" <<'PY'
import re, sys
m = re.fullmatch(r'glibc (\d+)\.(\d+)', sys.argv[1])
if not m or tuple(map(int, m.groups())) > (2, 31):
    raise SystemExit('Use a native Linux environment with glibc <= 2.31 (target Kylin or equivalent).')
PY

# Refuse reuse, root, relative paths and paths containing symlink components.
# Never delete a user's existing build tree. Failed builds remain for inspection.
work=$(python3 - "$1" <<'PY'
import os, pathlib, sys
p = pathlib.Path(sys.argv[1])
if not p.is_absolute() or '..' in p.parts or str(p) == '/':
    raise SystemExit('Build directory must be a new absolute path without .. components.')
if os.path.lexists(p) or p.resolve() != p:
    raise SystemExit('Build directory must not exist or contain symlink components.')
if not p.parent.is_dir():
    raise SystemExit('Build directory parent must already exist.')
print(p)
PY
)
mkdir -- "$work"
exec > >(tee "$work/build.log") 2>&1
trap 'printf "Build failed; diagnostic files retained in %s\n" "$work" >&2' ERR
jobs=${JOBS:-2}
[[ $jobs =~ ^[1-9][0-9]*$ ]] || die 'JOBS must be a positive integer.'
printf 'Native build: %s; %s; directory %s\n' "$rid" "$glibc" "$work"

# Compiler must support C++20 and static C++ runtime linking on this older glibc.
cat > "$work/compiler-check.cpp" <<'CPP'
#include <concepts>
#include <string>
template<class T> requires std::integral<T> int value(T n) { return n; }
int main() { std::string s = "cxx20"; return value(s.size()) == 5 ? 0 : 1; }
CPP
"$cxx" -std=c++20 -static-libstdc++ -static-libgcc "$work/compiler-check.cpp" -o "$work/compiler-check"
"$work/compiler-check"

commit=a390f59aa0b2448e08b1eb2604507f5f5822c7da
source="$work/opencvsharp"
git init "$source"
git -C "$source" remote add origin https://github.com/shimat/opencvsharp.git
git -C "$source" fetch --depth=1 origin "$commit"
git -C "$source" checkout --detach FETCH_HEAD
[[ $(git -C "$source" rev-parse HEAD) == "$commit" ]] || die 'OpenCvSharp commit mismatch.'
# Gitlink comes from the pinned parent commit; never follow a branch or remote tag.
opencv_commit=$(git -C "$source" rev-parse HEAD:opencv)
git -C "$source" submodule update --init --depth=1 -- opencv
[[ $(git -C "$source/opencv" rev-parse HEAD) == "$opencv_commit" ]] || die 'OpenCV submodule commit mismatch.'

prefix="$work/opencv-install"
cmake -S "$source/opencv" -B "$work/opencv-build" -G Ninja \
  -DCMAKE_BUILD_TYPE=Release -DCMAKE_INSTALL_PREFIX="$prefix" \
  -DCMAKE_C_COMPILER="$cc" -DCMAKE_CXX_COMPILER="$cxx" \
  -DCMAKE_CXX_STANDARD=20 -DCMAKE_POSITION_INDEPENDENT_CODE=ON \
  -DCMAKE_ASM_COMPILER:FILEPATH= \
  -DBUILD_SHARED_LIBS=OFF -DBUILD_LIST=core,imgproc,imgcodecs,videoio,objdetect,dnn \
  -DBUILD_TESTS=OFF -DBUILD_PERF_TESTS=OFF -DBUILD_EXAMPLES=OFF -DBUILD_opencv_apps=OFF \
  -DBUILD_opencv_python2=OFF -DBUILD_opencv_python3=OFF -DBUILD_JAVA=OFF \
  -DWITH_V4L=ON -DWITH_FFMPEG=OFF -DWITH_GSTREAMER=OFF -DWITH_1394=OFF \
  -DWITH_GTK=OFF -DWITH_QT=OFF -DWITH_OPENGL=OFF -DWITH_OPENCL=OFF \
  -DWITH_CUDA=OFF -DWITH_CUDNN=OFF -DWITH_TBB=OFF -DWITH_OPENMP=OFF \
  -DWITH_IPP=OFF -DWITH_ITT=OFF -DWITH_LAPACK=OFF -DWITH_EIGEN=OFF \
  -DWITH_TESSERACT=OFF -DWITH_ONNXRUNTIME=OFF -DDOWNLOAD_ONNXRUNTIME=OFF \
  -DWITH_PROTOBUF=ON -DBUILD_PROTOBUF=ON \
  -DWITH_JPEG=ON -DBUILD_JPEG=ON -DWITH_PNG=ON -DBUILD_PNG=ON -DBUILD_ZLIB=ON \
  -DWITH_TIFF=OFF -DWITH_WEBP=OFF -DWITH_OPENEXR=OFF -DWITH_OPENJPEG=OFF \
  -DWITH_JASPER=OFF -DWITH_AVIF=OFF -DWITH_GDAL=OFF -DWITH_GDCM=OFF \
  -DOPENCV_DNN_OPENCL=OFF -DOPENCV_DNN_CUDA=OFF
cmake --build "$work/opencv-build" --parallel "$jobs"
cmake --install "$work/opencv-build"

# Locate installed config without assuming OpenCV's major-version directory name.
opencv_config=$(find "$prefix" -name OpenCVConfig.cmake -print -quit)
[[ -n $opencv_config ]] || die 'Installed OpenCVConfig.cmake not found.'
opencv_dir=$(dirname "$opencv_config")
cmake -S "$source/src" -B "$work/extern-build" -G Ninja \
  -DCMAKE_BUILD_TYPE=Release -DCMAKE_C_COMPILER="$cc" -DCMAKE_CXX_COMPILER="$cxx" \
  -DOpenCV_DIR="$opencv_dir" -DOpenCV_STATIC=ON -DCMAKE_POSITION_INDEPENDENT_CODE=ON \
  '-DCMAKE_SHARED_LINKER_FLAGS=-static-libstdc++ -static-libgcc -Wl,-z,defs' \
  -DCMAKE_DISABLE_FIND_PACKAGE_Tesseract=ON \
  -DNO_CONTRIB=ON -DNO_STITCHING=ON -DNO_GEOMETRY=ON -DNO_CALIB=ON \
  -DNO_STEREO=ON -DNO_PTCLOUD=ON -DNO_VIDEO=ON -DNO_FEATURES=ON \
  -DNO_FLANN=ON -DNO_ML=ON -DNO_PHOTO=ON -DNO_BARCODE=ON -DNO_HIGHGUI=ON \
  -DNO_DNN=OFF -DNO_OBJDETECT=OFF -DNO_VIDEOIO=OFF
cmake --build "$work/extern-build" --parallel "$jobs"
library="$work/extern-build/OpenCvSharpExtern/libOpenCvSharpExtern.so"
[[ -f $library ]] || die 'Expected native library was not produced.'

# Smoke the actual wrapper with eager relocation, not just the underlying OpenCV.
# Validate every imported GLIBC symbol and reject unexpected shared dependencies.
python3 - "$library" "$elf_machine" "$work" <<'PY'
import ctypes, os, pathlib, re, subprocess, sys
lib, machine, work = sys.argv[1:]
def readelf(*args):
    return subprocess.check_output(['readelf', *args, lib], text=True)
header, dynamic, versions = readelf('-h'), readelf('-d'), readelf('--version-info')
pathlib.Path(work, 'native-readelf.txt').write_text(header + dynamic + versions)
if not re.search(r'Machine:\s*' + re.escape(machine) + r'\s*$', header, re.M):
    raise SystemExit('Produced ELF architecture does not match the native host.')
needed = re.findall(r'\(NEEDED\).*?\[(.*?)\]', dynamic)
allowed = {'libc.so.6', 'libm.so.6', 'libpthread.so.0', 'libdl.so.2', 'librt.so.1',
           'ld-linux-aarch64.so.1', 'ld-linux-x86-64.so.2'}
unexpected = set(needed) - allowed
if unexpected:
    raise SystemExit('Unexpected shared dependencies (do not ship blindly): ' + ', '.join(sorted(unexpected)))
required = [tuple(map(int, v.split('.'))) for v in re.findall(r'GLIBC_(\d+(?:\.\d+)+)', versions)]
if not required or max(required) > (2, 31):
    raise SystemExit('Output requires GLIBC newer than 2.31, or no GLIBC versions could be verified.')
if 'GLIBC_PRIVATE' in versions:
    raise SystemExit('Output unexpectedly references private GLIBC symbols.')
native = ctypes.CDLL(lib, mode=os.RTLD_NOW | os.RTLD_LOCAL)
native.core_Mat_sizeof.argtypes = []
native.core_Mat_sizeof.restype = ctypes.c_uint64
size = native.core_Mat_sizeof()
if not 0 < size < 4096:
    raise SystemExit('core_Mat_sizeof smoke failed.')
for name in ('objdetect_FaceDetectorYN_create', 'dnn_readNetFromONNX', 'videoio_VideoCapture_new1', 'imgcodecs_imencode_vector'):
    getattr(native, name)  # Missing required binding is fatal.
pathlib.Path(work, 'native-smoke.txt').write_text(
    f'core_Mat_sizeof={size}\nGLIBC maximum={".".join(map(str, max(required)))}\nNEEDED={needed}\n')
PY

# Verify compiled-in V4L2 and JPEG support using the exact static OpenCV install.
mkdir "$work/smoke-source"
cat > "$work/smoke-source/CMakeLists.txt" <<'CMAKE'
cmake_minimum_required(VERSION 3.15)
project(KylinOpenCvSmoke LANGUAGES CXX)
set(CMAKE_CXX_STANDARD 20)
find_package(OpenCV REQUIRED)
add_executable(opencv-smoke main.cpp)
target_include_directories(opencv-smoke PRIVATE ${OpenCV_INCLUDE_DIRS})
target_link_libraries(opencv-smoke PRIVATE ${OpenCV_LIBS})
target_link_options(opencv-smoke PRIVATE -static-libstdc++ -static-libgcc)
CMAKE
cat > "$work/smoke-source/main.cpp" <<'CPP'
#include <opencv2/core.hpp>
#include <opencv2/imgcodecs.hpp>
#include <iostream>
#include <regex>
int main() {
    const std::string info = cv::getBuildInformation();
    std::cout << info;
    if (!std::regex_search(info, std::regex("v4l/v4l2:[ \\t]+YES"))) return 1;
    cv::Mat frame(32, 32, CV_8UC3, cv::Scalar(32, 64, 128));
    std::vector<unsigned char> jpeg;
    if (!cv::imencode(".jpg", frame, jpeg) || jpeg.size() < 4) return 2;
    const cv::Mat decoded = cv::imdecode(jpeg, cv::IMREAD_COLOR);
    if (decoded.empty() || decoded.rows != 32 || decoded.cols != 32) return 3;
    return 0;
}
CPP
cmake -S "$work/smoke-source" -B "$work/smoke-build" -G Ninja \
  -DCMAKE_BUILD_TYPE=Release -DCMAKE_CXX_COMPILER="$cxx" -DOpenCV_DIR="$opencv_dir"
cmake --build "$work/smoke-build" --parallel "$jobs"
"$work/smoke-build/opencv-smoke" > "$work/opencv-build-info.txt"

# Stage only after checks pass. Keep full bundled third-party source notices so
# redistribution review can identify every static dependency and its license.
out="$work/artifact"
mkdir -p "$out/licenses/opencv" "$out/licenses/opencvsharp"
cp "$library" "$out/"
cp "$source/LICENSE" "$out/licenses/opencvsharp/"
cp "$source/opencv/LICENSE" "$out/licenses/opencv/"
python3 - "$source/opencv/3rdparty" "$out/licenses/opencv/3rdparty" <<'PY'
import pathlib, shutil, sys
source, dest = map(pathlib.Path, sys.argv[1:])
for path in source.rglob('*'):
    if path.is_file() and any(s in path.name.lower() for s in ('license', 'licence', 'copyright', 'notice', 'copying')):
        target = dest / path.relative_to(source)
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(path, target)
PY
cp "$work/native-readelf.txt" "$work/native-smoke.txt" "$work/opencv-build-info.txt" "$out/"
cp "$work/opencv-build/CMakeCache.txt" "$out/opencv-CMakeCache.txt"
cp "$work/extern-build/CMakeCache.txt" "$out/extern-CMakeCache.txt"
{
  printf 'status=native-built-and-smoked\nrid=%s\nhost_glibc=%s\n' "$rid" "$glibc"
  printf 'opencvsharp_commit=%s\nopencv_commit=%s\n' "$commit" "$opencv_commit"
  printf 'opencvsharp_managed_package=5.0.0.20260806\n'
  printf 'built_utc=%s\n' "$(date -u +%FT%TZ)"
  uname -a
  "$cc" --version
  "$cxx" --version
  cmake --version
} > "$out/build-info.txt"
cp "$0" "$out/build-kylin-native.sh"
(cd "$out" && find . -type f ! -name SHA256SUMS -print0 | sort -z | xargs -0 sha256sum > SHA256SUMS)
printf 'Native artifact ready: %s\nTarget camera and model-inference acceptance is still required.\n' "$out"
