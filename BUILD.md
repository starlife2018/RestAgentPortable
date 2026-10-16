# Как собрать RestAgentPortable в EXE после изменений

## Что потребуется

- 64-разрядная Windows 10/11 и Windows PowerShell 5.1 (`powershell.exe`).
- .NET Framework 4.7.2 или новее. Скрипт использует компилятор из `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`.
- Полная папка исходников проекта, включая `vendor`, `Main.xaml`, `App.png`, `Json.xshd`, `HttpFieldRegistry.csv` и `RestAgentPortable.exe.config`.

Visual Studio, .NET SDK и восстановление пакетов NuGet не требуются. Библиотека AvalonEdit уже находится в `vendor`, поэтому сборка работает без Интернета. Права администратора не нужны, если папка проекта доступна для записи.

## 1. Сохраните изменения и закройте приложение

Сохраните изменённые исходники в редакторе. Если запущен `RestAgentPortable.exe` из папки проекта, закройте его: Windows не разрешит перезаписать работающий EXE.

Откройте PowerShell и перейдите в папку проекта:

```powershell
Set-Location -LiteralPath 'F:\1. Projects\RestAgentPortable'
```

Если проект лежит в другом месте, подставьте свой путь. Все команды ниже выполняются из корня проекта.

## 2. Соберите EXE

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1
if ($LASTEXITCODE -ne 0) { throw 'Сборка завершилась с ошибкой. Прочитайте сообщения выше.' }
```

`-ExecutionPolicy Bypass` действует для запускаемого процесса PowerShell; постоянные настройки системы эта команда не меняет.

При успехе появится сообщение:

```text
Build complete: RestAgentPortable.exe
```

В корне проекта будет новый `RestAgentPortable.exe`. Скрипт заново создаёт `App.ico` из `App.png`, компилирует C# и встраивает в EXE разметку, иконку, каталог заголовков, подсветку, библиотеку редактора и её лицензию.

Если компилятор сообщил об ошибке, сборку нельзя считать готовой: старый EXE может оставаться в папке. Исправьте указанный файл и строку, затем повторите команду.

## 3. Запустите самопроверку

```powershell
$testProcess = Start-Process -FilePath .\RestAgentPortable.exe -ArgumentList '--self-test' -WindowStyle Hidden -Wait -PassThru
Get-Content -LiteralPath .\test-results.txt -Encoding UTF8
if ($testProcess.ExitCode -ne 0) { throw 'Самотест не пройден. Проверьте test-results.txt.' }
```

Успешный результат: код завершения `0`, последняя строка отчёта — `ALL TESTS PASSED`.

Самотест проверяет интерфейс, коллекции, параметры URL, редакторы, Unicode и HTTP-запросы к временному серверу на localhost. Рабочая коллекция в `data` не используется. В папке EXE появляются `test-results.txt` и изображения проверяемого интерфейса.

После самотеста откройте приложение и вручную проверьте изменённую вами функцию:

```powershell
Start-Process -FilePath .\RestAgentPortable.exe
```

## 4. Подготовьте готовое приложение для переноса

Для запуска нужны два файла рядом друг с другом:

```text
RestAgentPortable.exe
RestAgentPortable.exe.config
```

Пользовательские данные приложение хранит в папке `data` рядом с EXE. При обновлении существующей установки закройте приложение и замените EXE и `.config`, сохранив прежнюю папку `data`. На новом компьютере также требуется .NET Framework 4.7.2 или новее.

Чтобы упаковать чистую сборку с инструкциями в ZIP, выполните:

```powershell
Compress-Archive -LiteralPath '.\RestAgentPortable.exe', '.\RestAgentPortable.exe.config', '.\README.md', '.\BUILD.md' -DestinationPath '.\RestAgentPortable-Windows.zip' -Force
```

Эта команда заменяет предыдущий ZIP с таким именем. Папка `data`, история и сохранённые запросы в архив не попадают. `Build.ps1` сам по себе ZIP не создаёт и в GitHub ничего не отправляет.

## Если основной EXE сейчас нельзя закрыть

Можно собрать проверочную копию с тем же именем в отдельной папке:

```powershell
New-Item -ItemType Directory -Path '.\build-check' -Force | Out-Null
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1 -OutputName 'build-check\RestAgentPortable.exe'
if ($LASTEXITCODE -ne 0) { throw 'Проверочная сборка завершилась с ошибкой.' }
Copy-Item -LiteralPath '.\RestAgentPortable.exe.config' -Destination '.\build-check\RestAgentPortable.exe.config' -Force
$testProcess = Start-Process -FilePath '.\build-check\RestAgentPortable.exe' -ArgumentList '--self-test' -WindowStyle Hidden -Wait -PassThru
Get-Content -LiteralPath '.\build-check\test-results.txt' -Encoding UTF8
if ($testProcess.ExitCode -ne 0) { throw 'Самотест проверочной сборки не пройден.' }
```

Основной EXE эта команда не заменяет. Когда приложение можно будет закрыть, выполните обычную сборку из шага 2. Результаты самотеста проверочной копии находятся в `build-check`.

## Частые ошибки

| Сообщение или ситуация | Что сделать |
| --- | --- |
| `.NET Framework compiler not found.` | Проверьте наличие компилятора по пути из раздела «Что потребуется». Установите или восстановите .NET Framework на 64-разрядной Windows. |
| `CS0016`, доступ к EXE запрещён или файл занят | Закройте приложение из папки проекта, проверьте права записи или соберите проверочную копию в отдельной папке. |
| Не найдена `ICSharpCode.AvalonEdit.dll` или ресурс | Восстановите полную папку исходников. DLL должна быть в `vendor\AvalonEdit\lib\net462`, а файлы ресурсов — в корне проекта. |
| Ошибка C# с именем файла и номером строки | Исправьте ошибку в исходнике и повторите сборку. |
| Сборка успешна, но самотест завершился с ошибкой | Откройте свежий `test-results.txt` рядом с проверяемым EXE и найдите `FAIL`. Исправьте причину перед использованием новой сборки. |
| После переноса EXE приложение работает неправильно | Проверьте, что рядом лежит соответствующий `RestAgentPortable.exe.config`, установлен нужный .NET Framework и папка доступна для записи. |
