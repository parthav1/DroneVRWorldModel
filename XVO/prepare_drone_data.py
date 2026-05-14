from drone_pose_utils import build_argparser, prepare_drone_bundle


if __name__ == "__main__":
    prepare_drone_bundle(build_argparser().parse_args())
