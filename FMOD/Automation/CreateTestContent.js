(function() {
    var event2DPath = "event:/Test 2D Tone";
    var event3DPath = "event:/Test 3D Tone";

    if (studio.project.lookup(event2DPath) || studio.project.lookup(event3DPath)) {
        throw new Error("Test events already exist; refusing to create duplicate content.");
    }

    var workspacePath = studio.project.workspace._filePath.replace(/\\/g, "/");
    var projectRoot = workspacePath.substring(0, workspacePath.lastIndexOf("/Metadata/Workspace.xml"));
    var sourcePath = projectRoot + "/../SourceAudio/TestTone.wav";
    var audioFile = studio.project.importAudioFile(sourcePath);
    var masterBank = studio.project.lookup("bank:/Master");

    if (!audioFile) {
        throw new Error("Failed to import test tone from " + sourcePath);
    }

    if (!masterBank) {
        throw new Error("Master bank was not found.");
    }

    function createToneEvent(name, withSpatializer) {
        var event = studio.project.workspace.addEvent(name, withSpatializer);
        var track = event.addGroupTrack("Tone");
        var sound = track.addSound(event.timeline, "SingleSound", 0, 2);

        sound.audioFile = audioFile;
        event.relationships.banks.add(masterBank);
        event.note = withSpatializer
            ? "FMOD Unity integration smoke-test event with an FMOD Spatializer."
            : "FMOD Unity integration smoke-test event without spatial processing.";

        return event;
    }

    var event2D = createToneEvent("Test 2D Tone", false);
    var event3D = createToneEvent("Test 3D Tone", true);

    if (event2D.is3D()) {
        throw new Error("The 2D test event was unexpectedly detected as 3D.");
    }

    if (!event3D.is3D()) {
        throw new Error("The 3D test event is missing spatial behavior.");
    }

    if (!studio.project.save()) {
        throw new Error("Failed to save FMOD test content.");
    }

    console.log("Created " + event2D.getPath() + " (2D) and " + event3D.getPath() + " (3D).");
})();
