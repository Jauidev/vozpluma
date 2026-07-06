@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

echo ============================================
echo   Instalador del Agente de Voz
echo ============================================
echo.

rem ---- 1. Comprobar Python ----
python --version >nul 2>nul
if errorlevel 1 (
    echo Python no encontrado. Instalando Python 3.12 con winget...
    winget install -e --id Python.Python.3.12 --accept-source-agreements --accept-package-agreements
    echo.
    echo Python instalado. CIERRA esta ventana y vuelve a ejecutar instalar.bat
    echo para que Windows encuentre el nuevo Python.
    pause
    exit /b
)
for /f "tokens=*" %%v in ('python --version') do echo Detectado: %%v

rem ---- 2. Crear entorno virtual ----
if not exist ".venv" (
    echo Creando entorno virtual...
    python -m venv .venv
)

rem ---- 3. Instalar PyTorch (con CUDA si hay GPU NVIDIA) ----
nvidia-smi >nul 2>nul
if errorlevel 1 (
    echo No se detecta GPU NVIDIA: instalando PyTorch para CPU...
    .venv\Scripts\python.exe -m pip install torch torchaudio
) else (
    echo GPU NVIDIA detectada: instalando PyTorch con CUDA 12.6...
    .venv\Scripts\python.exe -m pip install torch torchaudio --index-url https://download.pytorch.org/whl/cu126
)
if errorlevel 1 goto :error

rem ---- 4. Resto de dependencias ----
echo Instalando dependencias de audio y modelos...
.venv\Scripts\python.exe -m pip install --upgrade pip >nul
.venv\Scripts\python.exe -m pip install -r requirements.txt
if errorlevel 1 goto :error

echo.
echo ============================================
echo   Instalacion completada.
echo   Abre ui\VoiceAgent.exe para empezar.
echo   (la primera vez descargara el modelo de voz, ~1.5 GB)
echo ============================================
pause
exit /b

:error
echo.
echo Ha fallado la instalacion. Revisa el mensaje de error de arriba.
pause
exit /b 1
