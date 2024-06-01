(Caution: This repository is currently under active development and quite unstable. Be aware of this before cloning.)
# Features:

* Path Tracing

* Multiple Importance Sampling, Russian Roulette

* Spatiotemporal Variance-Guided Filter

* Edge-avoiding À-Trous Wavelet Filter
  
* Fireflies mitigation
  
* Simple TAA

* Field of Depth

* BVH
* Metallic workflow

* Refraction, Disney BRDF, Phong

* GBuffer

* Skybox
  
* Unity

* Adjustable SPP/frame ((1 spp to 32 spp per frame)

# Demo
* path tracer + taa + skybox + metallic workflow + refraction

<img width="772" alt="image" src="https://github.com/zhuzhanji/Path-Tracing/assets/37281560/7799cd0b-d549-41db-9bba-a9d07c80b351">

* Multiple importance sampling

Less noisy and faster to converge when the light source is small.
<img width="580" alt="image" src="https://github.com/zhuzhanji/Path-Tracing/assets/37281560/9dfa57eb-3508-402e-9916-fd07d8d14922">

* Denoisers
  
SVGF is more temporally stable than EAW.

https://github.com/zhuzhanji/Path-Tracing/assets/37281560/1de0f4e7-5310-4938-84b2-445f04fa5486


# Todo
  * Material: albedo map, normal map etc
  * SVGF albedo demodulation and modulation after albedo maps are added

# Reference

https://www.pbr-book.org/3ed-2018/contents

https://raytracing.github.io/books/RayTracingTheRestOfYourLife.html

Edge-avoiding a-trous wavelet transform for fast global illumination filtering.

https://bitbucket.org/Daerst/gpu-ray-tracing-in-unity/src/master/Assets/RayTracingMaster.cs
