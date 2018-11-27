## KMB_Image_Sync_Client: Downloads standard images from Picturepark and hash-compares them to local directory

Identical images will be ignored, different images replaced and archived.


#### Summary of the guidelines:

* Tool supports use of proxy, configured in the ..exe.config file (leave address empty to not use proxy)
* Both configuration files [..exe.config and Configuration.txt] have to be in the same folder as .exe
* First-time-run the tool will create a Completed.csv file, this file has to remain in the same folder as the .exe file and keeps a history of already checked images
* Use the "generate configuration file"-option to create an configuration file with default values
* You can find the precompiled program in the BUILD folder 