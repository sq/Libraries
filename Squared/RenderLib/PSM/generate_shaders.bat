@echo Generating shaders...
@mkdir generated
@mkdir generated\shaders
@for %%f IN (shaders\*.fcg) DO tcc.exe -E %%f -o generated\%%f
@for %%f IN (shaders\*.vcg) DO tcc.exe -E %%f -o generated\%%f
@echo Done.