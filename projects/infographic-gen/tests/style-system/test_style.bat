@echo off
echo.
echo ========================================
echo  Style System Live Test
echo ========================================
echo.

echo Creating test notebook...
powershell -Command "$body = @{title='StyleTest_Business';description='Testing business domain'} | ConvertTo-Json; $result = Invoke-RestMethod -Uri 'http://localhost:8400/api/notebooks' -Method Post -Body $body -ContentType 'application/json'; Write-Output $result.notebook.id" > notebook_id.txt
set /p NOTEBOOK_ID=<notebook_id.txt

echo Notebook ID: %NOTEBOOK_ID%
echo.

echo Adding business content source...
powershell -Command "$body = @{notebookId='%NOTEBOOK_ID%';title='Q4 Report';content='Our enterprise company achieved 25%% revenue growth in Q4. Key factors include corporate partnerships, market expansion in finance sector, and strategic management decisions for business success.'} | ConvertTo-Json; Invoke-RestMethod -Uri 'http://localhost:8400/api/notebook/sources/text' -Method Post -Body $body -ContentType 'application/json'"
echo.

echo Generating infographic with business keywords...
echo This will trigger domain detection: business
powershell -Command "$body = @{type='infographic'} | ConvertTo-Json; Invoke-RestMethod -Uri 'http://localhost:8400/api/studio/%NOTEBOOK_ID%/generate' -Method Post -Body $body -ContentType 'application/json'"
echo.

echo.
echo ========================================
echo  Test Complete!
echo ========================================
echo.
echo Check server logs for style detection output:
echo   [Style] Detected domain: business
echo   [Style] Selected variant: ...
echo.
echo Open http://localhost:8400/notebook to view the infographic
echo.

del notebook_id.txt
pause
