# Unity 3D Yacht Simulator
I decided to develop my own yacht simulator and want to share my findings of sailing physics and its implementation using the Unity game development platform. Though I read tons of blogs and books regarding ships & water physics there are almost no examples and explanations about sails & wind forces. So, I decided to do the research and post here the results.

[Click on the picture to see the video of simulation on "YouTube"](https://www.youtube.com/watch?v=0eu_4tyjstk). At the beginning of the video you see the blue and red vectors that presents lift & drag forces. Yellow vectors shows apparent wind angle. Green is the yacht velocity vector.
[![YouTube video about 3D Yacht Simulator](http://img.youtube.com/vi/0eu_4tyjstk/0.jpg)](https://www.youtube.com/watch?v=0eu_4tyjstk "3D Yacht Simulator v0.1")

In this project I would like to implement most important forces requred to simulate the real sailing yacht behavior.
* Apparent wind
* Wind lift & drag forces
* Water buoyancy force
* Hull viscous pressure resistance
* Hull friction resistance
* Keel lift & drag (resistance) forces
* Waves resistance
* Steering rudder lift & drag forces
* Hull air resistance
* Hull lateral resistance

Check the whole development story at Medium: https://vlytsus.medium.com/my-sailing-story-unity-game-development-part-1-6b6c5e7f3844

## Boatware bridge

Branch `boatware` adds `Assets/Boatware/SimBridge.cs`. It listens on `127.0.0.1:8767` for one JSON object per line and publishes telemetry the same way. The sailing model in `BoatForces` is unchanged.

`origin` is this fork. Add the original project once if it is missing:

```bash
git remote add upstream https://github.com/vlytsus/unity-3d-boat.git
```

Pull new upstream work onto `boatware` with `git fetch upstream` and `git merge upstream/main`. Push this fork with `git push origin boatware`.

Windowed macOS player (Unity **6000.3.13f1** on this branch; upstream `main` stays on 2019.4.11f1):

```bash
Unity -batchmode -quit -projectPath . -executeMethod BoatwareBuild.BuildMacPlayer -logFile -
```

The app is written to `Builds/boatware-sim.app` (gitignored). It opens `GameScene` directly.

Commands: `set_environment`, `set_origin`, `set_waypoints`, `set_helm`, `set_mode` (`scripted` or `interactive`), `reset`, `pause`, `resume`, `set_time_scale`. In `scripted` mode the keyboard and waypoint autopilot in `YachtsManager` are left alone so an external helm can drive the rudder.
