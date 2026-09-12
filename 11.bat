@echo off
rem 清理构建残留：删除 Assemblies 中除本模组 DLL 外的复制品，并移除 obj/
set "sub_folder=\Clap_Your_Hands\Assemblies"
set "exclude_file=Clap_Your_Hands.dll"
set "target_dir=%~dp0%sub_folder%"

pushd "%target_dir%"

for /f "delims=" %%i in ('dir /b /a-d ^| findstr /v /x "%exclude_file%"') do (
  del "%%i"
)

cd ..
rd obj /s /q

popd
pause
