import glob
import pandas as pd
import numpy as np
from PIL import Image
import torch
from torch.utils.data import Dataset, DataLoader
from torchvision import transforms
import random
from params import par
import os
from drone_pose_utils import pair_pose_vector

def get_data_info(training_data, data_path, threshold=-5.668):
    '''
    Args:
        data_training (dictionary)
        data_pth (dictionary)

    Return:
        DataFrame: ['imgs_paths', 'poses']
    '''
    imgs_paths = []
    poses = []
    audios_path = []
    masks_path = []

    for key in training_data.keys():
        # print(key)
        for scene in training_data[key]:
            # print(scene)
            scene_poses = list(np.load(f'./poses/{key}/{scene}.npy')) # Array: [N-1, 15]

            scene_imgs_path = glob.glob(f'{data_path[key]}/sequences/{scene}/image_2/*.jpg') + glob.glob(f'{data_path[key]}/sequences/{scene}/image_2/*.png')
            scene_imgs_path = sorted(scene_imgs_path) # List: N

            if par.multi_modal:
                mask_imgs_path = glob.glob(f'{data_path[key]}/segmentations/{scene}/image_2/*.jpg') + glob.glob(f'{data_path[key]}/segmentations/{scene}/image_2/*.png') + glob.glob(f'{data_path[key]}/segmentations/{scene}/image_2/*.jpeg')
                mask_imgs_path = sorted(mask_imgs_path)
            else:
                mask_imgs_path = ['./0.npy' for i in range(len(scene_imgs_path))]
            
            if key == 'YouTube':
                audio_left_path = glob.glob(f'{data_path[key]}/audio_fixed/{scene}/*_left.npy')
                audio_left_path = sorted(audio_left_path)
                audio_right_path = glob.glob(f'{data_path[key]}/audio_fixed/{scene}/*_right.npy')
                audio_right_path = sorted(audio_right_path)
                assert len(audio_left_path) == len(audio_right_path)
                audio_path_paried = []
                mask_imgs_path_paried = []
                scene_imgs_path_paired = []
                scene_poses_filter = []

                for i in range(len(scene_imgs_path)-1):
                    if scene_poses[i][-1] < threshold:
                        scene_imgs_path_paired.append([scene_imgs_path[i], scene_imgs_path[i+1]])
                        scene_poses_filter.append(scene_poses[i][:-1].tolist())
                        audio_path_paried.append([audio_left_path[i], audio_right_path[i]])
                        mask_imgs_path_paried.append([mask_imgs_path[i], mask_imgs_path[i+1]])

                poses = poses + scene_poses_filter
            else:
                scene_imgs_path_paired = [[scene_imgs_path[i], scene_imgs_path[i+1]] for i in range(len(scene_imgs_path)-1)] # List: N-1
                assert len(scene_imgs_path_paired) == len(scene_poses)
                mask_imgs_path_paried = [[mask_imgs_path[i], mask_imgs_path[i+1]] for i in range(len(mask_imgs_path)-1)]
                assert len(scene_imgs_path_paired) == len(mask_imgs_path_paried)
                audio_path_paried = [['./0.npy', './0.npy'] for _ in range(len(scene_imgs_path)-1)] # Just Nothing

                poses = poses + scene_poses

            imgs_paths = imgs_paths + scene_imgs_path_paired
            masks_path = masks_path + mask_imgs_path_paried
            audios_path = audios_path + audio_path_paried

    assert len(imgs_paths) == len(poses)

    data = {'imgs_paths': imgs_paths, 'poses': poses, 'audios_path': audios_path, 'masks_path':masks_path}
    df = pd.DataFrame(data, columns = ['imgs_paths', 'poses', 'audios_path', 'masks_path'])

    return df


class VisualOdometryDataset(Dataset):

    def __init__(self, data_df, img_size, test=False):
        '''
        Args:
            data_df (dataframe): Image paths and poses.
            img_size: (hight, width)
            transform (callable, optional): Optionally apply transform.

        Return:
            imgs_sequence: (channels, h, w)
            poses_gt: (pose)
        '''
        self.data_df = data_df
        self.test = test
        self.imgs_paths = self.data_df['imgs_paths'].to_numpy()
        self.poses = self.data_df['poses'].to_numpy()
        self.audios_path = self.data_df['audios_path'].to_numpy()
        self.masks_path = self.data_df['masks_path'].to_numpy()

        '''
        Transforms
        '''
        transform_ops = []
        transform_ops.append(transforms.Resize((img_size[0], img_size[1]), interpolation=transforms.InterpolationMode.BICUBIC))
        transform_ops.append(transforms.ToTensor())
        self.transformer = transforms.Compose(transform_ops)

        transform_mask = []
        transform_mask.append(transforms.Resize((225, 289), interpolation=transforms.InterpolationMode.BICUBIC))
        transform_mask.append(transforms.ToTensor())
        self.transformer_mask = transforms.Compose(transform_mask)

    def __len__(self):
        return len(self.data_df)

    def __getitem__(self, idx):
        if torch.is_tensor(idx):
            idx = idx.tolist()

        poses_gt = torch.FloatTensor(self.poses[idx])
        pth =  self.imgs_paths[idx]
        audio_pth =  self.audios_path[idx]
        mask_pth = self.masks_path[idx]

        if os.path.isfile(audio_pth[0]):
            audio_l = torch.from_numpy(np.load(audio_pth[0], allow_pickle=True))
            audio_r = torch.from_numpy(np.load(audio_pth[1], allow_pickle=True))
            audios = torch.stack((audio_l, audio_r), 0)
        else:
            audio_l = torch.from_numpy(np.array([-1.0 for _ in range(4410)]))
            audio_r = torch.from_numpy(np.array([-1.0 for _ in range(4410)]))
            audios = torch.stack((audio_l, audio_r), 0)

        img1 = Image.open(pth[0])
        img2 = Image.open(pth[1])
        if par.multi_modal:
            mask1 = Image.open(mask_pth[0])
            mask2 = Image.open(mask_pth[1])

        w1, h1 = img1.size
        w2, h2 = img2.size
        assert w1 == w2
        assert h1 == h2

        if self.test:
            if 'KITTI' in pth[0]:
                k = 1
                h = 0
                w = (184+384)/2
                img1 = transforms.functional.crop(img1, h, w, 370*k, 658*k)
                img2 = transforms.functional.crop(img2, h, w, 370*k, 658*k)
            else:
                pass
        else:
            if 'KITTI' in pth[0]:
                k = 1
                h = 0
                w = random.uniform(184, 384)
                img1 = transforms.functional.crop(img1, h, w, 370*k, 658*k)
                img2 = transforms.functional.crop(img2, h, w, 370*k, 658*k)
            else:
                # k = random.uniform(par.scale, 1)
                k = random.choices([1, random.uniform(par.scale[0], par.scale[1])], [0.3, 0.7])[0]
                h = random.uniform(0, h1*(1-k))
                w = random.uniform(0, w1*(1-k))
                img1 = transforms.functional.crop(img1, h, w, h1*k, w1*k)
                img2 = transforms.functional.crop(img2, h, w, h1*k, w1*k)
                mask1 = transforms.functional.crop(mask1, h, w, h1*k, w1*k)
                mask2 = transforms.functional.crop(mask2, h, w, h1*k, w1*k)


        img1 = self.transformer(img1)
        img2 = self.transformer(img2)        
        imgs = torch.cat((img1, img2), 0)
        if par.multi_modal:
            mask1 = self.transformer_mask(mask1)
            mask2 = self.transformer_mask(mask2)
            masks = torch.cat((mask1, mask2), 0)
        else:
            masks = 0

        if self.test:
            return (pth[0], imgs, poses_gt, audios, masks)
        else:
            return (imgs, poses_gt, audios, masks)
    

def get_test_data_info(data_testing, data_pth):
    
    imgs_path = []

    for key in data_testing.keys():
        for scene in data_testing[key]:
            scene_imgs_path = glob.glob(f'{data_pth[key]}/sequences/{scene}/image_2/*.jpg') + glob.glob(f'{data_pth[key]}/sequences/{scene}/image_2/*.png')
            scene_imgs_path = sorted(scene_imgs_path)
            scene_imgs_path_paired = [[scene_imgs_path[i], scene_imgs_path[i+1]] for i in range(len(scene_imgs_path)-1)]
            imgs_path = imgs_path + scene_imgs_path_paired
    data = {'imgs_path': imgs_path}
    df = pd.DataFrame(data, columns = ['imgs_path'])

    return df

class TestDataset(Dataset):

    def __init__(self, data_df, img_size):

        self.data_df = data_df
        self.imgs_path = np.asarray(self.data_df.imgs_path)

        transform_ops = []
        transform_ops.append(transforms.Resize((img_size[0], img_size[1]), interpolation=transforms.InterpolationMode.BICUBIC))
        transform_ops.append(transforms.ToTensor())
        self.transformer = transforms.Compose(transform_ops)

    def __len__(self):
        return len(self.data_df)

    def __getitem__(self, idx):
        if torch.is_tensor(idx):
            idx = idx.tolist()

        pth = self.imgs_path[idx]
        img1 = Image.open(pth[0])
        img2 = Image.open(pth[1])
        w1, h1 = img1.size
        w2, h2 = img2.size
        assert w1 == w2
        assert h1 == h2

        # We do crop is because KITTI dataset is a bit old, img size is around 1241*376
        if 'KITTI' in pth[0]:
            k = 1
            h = 0
            w = (184+384)/2
            img1 = transforms.functional.crop(img1, h, w, 370*k, 658*k)
            img2 = transforms.functional.crop(img2, h, w, 370*k, 658*k)
        else:
            pass

        img1 = self.transformer(img1)
        img2 = self.transformer(img2)   
        imgs = torch.cat((img1, img2), 0)

        return (pth[0], imgs)


class DroneVODataset(Dataset):

    def __init__(self, pair_csv, img_size, train=False, return_dict=True, random_crop=True):
        '''
        Drone supervised VO dataset.

        Args:
            pair_csv: CSV produced by drone_pose_utils.py. Labels are motion from
                image_i to image_j in xyzw quaternion order.
            img_size: (height, width)
            train: enables paired random crop augmentation.
            return_dict: when False, returns the legacy XVO tuple
                (imgs, pose_vector, audios, masks). The first 15 pose values are
                tx,ty,tz,euler_xyz_deg,R_flat, matching the original convention.
        '''
        self.data_df = pd.read_csv(pair_csv)
        self.img_size = img_size
        self.train = train
        self.return_dict = return_dict
        self.random_crop = random_crop

        transform_ops = []
        transform_ops.append(transforms.Resize((img_size[0], img_size[1]), interpolation=transforms.InterpolationMode.BICUBIC))
        transform_ops.append(transforms.ToTensor())
        self.transformer = transforms.Compose(transform_ops)

    def __len__(self):
        return len(self.data_df)

    def _load_pair(self, row):
        img1 = Image.open(row.image_i).convert('RGB')
        img2 = Image.open(row.image_j).convert('RGB')
        w1, h1 = img1.size
        w2, h2 = img2.size
        assert w1 == w2
        assert h1 == h2

        if self.train and self.random_crop:
            k = random.choices([1, random.uniform(par.scale[0], par.scale[1])], [0.3, 0.7])[0]
            h = random.uniform(0, h1 * (1 - k))
            w = random.uniform(0, w1 * (1 - k))
            img1 = transforms.functional.crop(img1, h, w, h1 * k, w1 * k)
            img2 = transforms.functional.crop(img2, h, w, h1 * k, w1 * k)

        img1 = self.transformer(img1)
        img2 = self.transformer(img2)
        return torch.cat((img1, img2), 0)

    def __getitem__(self, idx):
        if torch.is_tensor(idx):
            idx = idx.tolist()

        row = self.data_df.iloc[idx]
        imgs = self._load_pair(row)
        pose = torch.from_numpy(pair_pose_vector(row))

        if not self.return_dict:
            audios = torch.zeros(2, 4410, dtype=torch.float32)
            masks = torch.zeros(1, dtype=torch.float32)
            return (imgs, pose, audios, masks)

        return {
            'imgs': imgs,
            'pose': pose,
            'sequence_id': row['sequence_id'],
            'frame_i': int(row['frame_i']),
            'frame_j': int(row['frame_j']),
            'timestamp_i': float(row['timestamp_i']),
            'timestamp_j': float(row['timestamp_j']),
            'dt': float(row['dt']),
            'image_i': row['image_i'],
            'image_j': row['image_j'],
            'source': row['source'],
        }
