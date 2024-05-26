# Features:

* Path Tracing

* Monte Carlo Importance Sampling, Russian Roulette

* Spatiotemporal Variance-Guided Filter

* Edge-avoiding À-Trous Wavelet Filter
  
* Neighbourhood color clamp to mitigate fireflies
  
* Simple TAA

* Field of Depth

* BVH

* Refraction, Disney BRDF, Phong

* GBuffer

* Skybox
* 
* Unity

* Adjustable SPP/frame ((1 spp to 32 spp per frame)

# Demo

<img width="772" alt="image" src="https://github.com/zhuzhanji/Path-Tracing/assets/37281560/7799cd0b-d549-41db-9bba-a9d07c80b351">


<img width="809" alt="Screenshot 2024-05-14 at 08 15 10" src="https://github.com/zhuzhanji/Path-Tracing/assets/37281560/c6f86c87-0f72-4f29-992b-c8f192dc5eb7">



## With depth of filed on.

<img width="808" alt="Screenshot 2024-05-14 at 08 19 34" src="https://github.com/zhuzhanji/Path-Tracing/assets/37281560/1a00088c-5297-4147-a3cc-7604537810fb">

# Todo
  * Material: albedo map, normal map etc
  * SVGF albedo demodulation and modulation after albedo maps are added

# Reference

https://www.pbr-book.org/3ed-2018/contents

https://raytracing.github.io/books/RayTracingTheRestOfYourLife.html

Edge-avoiding a-trous wavelet transform for fast global illumination filtering.

https://bitbucket.org/Daerst/gpu-ray-tracing-in-unity/src/master/Assets/RayTracingMaster.cs
